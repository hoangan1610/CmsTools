using System;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Services
{
    public interface IAuditLogger
    {
        Task LogLoginAsync(
            HttpContext httpContext,
            string username,
            bool isSuccess,
            string? message = null);

        Task LogActionAsync(
            HttpContext httpContext,
            string action,
            string? entity = null,
            long? entityId = null,
            object? details = null);
    }

    public sealed class AuditLogger : IAuditLogger
    {
        private readonly string _metaConn;
        private readonly JsonSerializerOptions _jsonOptions;

        public AuditLogger(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        private static (int? cmsUserId, string? username) GetCurrentUser(HttpContext httpContext)
        {
            int? cmsUserId = null;
            string? username = null;

            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var idClaim = user.FindFirst("cms_user_id");
                if (idClaim != null && int.TryParse(idClaim.Value, out var id))
                    cmsUserId = id;

                username = user.Identity?.Name
                           ?? user.FindFirst(ClaimTypes.Name)?.Value;
            }

            return (cmsUserId, username);
        }

        private static (string? ip, string? ua) GetRequestInfo(HttpContext httpContext)
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString();

            // ưu tiên X-Forwarded-For nếu có proxy
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd))
            {
                var s = fwd.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    ip = s.Split(',')[0].Trim();
                }
            }

            var ua = httpContext.Request.Headers["User-Agent"].ToString();
            if (!string.IsNullOrEmpty(ua) && ua.Length > 400)
                ua = ua.Substring(0, 400); // cột NVARCHAR(400)

            return (ip, ua);
        }

        // ===================== LOGIN LOG =====================

        public async Task LogLoginAsync(
            HttpContext httpContext,
            string username,
            bool isSuccess,
            string? message = null)
        {
            using var conn = OpenMeta();

            var (cmsUserId, _) = GetCurrentUser(httpContext);
            var (ip, ua) = GetRequestInfo(httpContext);

            const string sql = @"
INSERT INTO dbo.tbl_cms_login_log(
    cms_user_id,
    username,
    is_success,
    ip,
    user_agent,
    message
) VALUES(
    @CmsUserId,
    @Username,
    @IsSuccess,
    @Ip,
    @UserAgent,
    @Message
);";

            await conn.ExecuteAsync(sql, new
            {
                CmsUserId = cmsUserId,
                Username = username,       // username nhập ở form login
                IsSuccess = isSuccess,
                Ip = ip,
                UserAgent = ua,
                Message = message
            });
        }

        // ===================== ACTION AUDIT =====================

        public async Task LogActionAsync(
            HttpContext httpContext,
            string action,
            string? entity = null,
            long? entityId = null,
            object? details = null)
        {
            using var conn = OpenMeta();

            var (cmsUserId, username) = GetCurrentUser(httpContext);
            var (ip, ua) = GetRequestInfo(httpContext);

            // Serialize details -> new_values
            string? json = null;
            if (details != null)
            {
                try
                {
                    json = JsonSerializer.Serialize(details, _jsonOptions);
                }
                catch
                {
                    // ignore JSON error, vẫn log được
                }
            }

            // map sang schema hiện tại của tbl_cms_audit_log
            // operation: nvarchar(50)
            var operation = string.IsNullOrWhiteSpace(action)
                ? "unknown"
                : action.Trim();
            if (operation.Length > 50)
                operation = operation.Substring(0, 50);

            // connection_name: nvarchar(100)
            var connectionName = "CmsToolsDb";

            // schema_name, table_name
            var schemaName = "dbo";
            var tableName = string.IsNullOrWhiteSpace(entity)
                ? "N/A"
                : entity.Trim();
            if (tableName.Length > 128)
                tableName = tableName.Substring(0, 128);

            // primary key info (nếu có)
            string? pkColumn = null;
            string? pkValue = null;
            if (entityId.HasValue)
            {
                pkColumn = "id";
                pkValue = entityId.Value.ToString();
                if (pkValue.Length > 256)
                    pkValue = pkValue.Substring(0, 256);
            }

            const string sql = @"
INSERT INTO dbo.tbl_cms_audit_log(
    user_id,
    operation,
    connection_name,
    schema_name,
    table_name,
    primary_key_column,
    primary_key_value,
    ip_address,
    user_agent,
    old_values,
    new_values
) VALUES(
    @UserId,
    @Operation,
    @ConnectionName,
    @SchemaName,
    @TableName,
    @PrimaryKeyColumn,
    @PrimaryKeyValue,
    @IpAddress,
    @UserAgent,
    @OldValues,
    @NewValues
);";

            await conn.ExecuteAsync(sql, new
            {
                UserId = cmsUserId,
                Operation = operation,
                ConnectionName = connectionName,
                SchemaName = schemaName,
                TableName = tableName,
                PrimaryKeyColumn = pkColumn,
                PrimaryKeyValue = pkValue,
                IpAddress = ip,
                UserAgent = ua,
                OldValues = (string?)null,   // chưa diff trước/sau
                NewValues = json            // JSON của details
            });
        }
    }
}
