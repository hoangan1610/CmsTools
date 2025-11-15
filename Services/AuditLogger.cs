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
        Task LogLoginAsync(HttpContext httpContext, string username, bool isSuccess, string? message = null);
        Task LogActionAsync(HttpContext httpContext, string action, string? entity = null, long? entityId = null, object? details = null);

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

            // nếu có X-Forwarded-For (reverse proxy) thì ưu tiên
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd))
            {
                var s = fwd.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    ip = s.Split(',')[0].Trim();
                }
            }

            var ua = httpContext.Request.Headers["User-Agent"].ToString();
            if (ua?.Length > 512)
                ua = ua.Substring(0, 512);

            return (ip, ua);
        }

        public async Task LogLoginAsync(HttpContext httpContext, string username, bool isSuccess, string? message = null)
        {
            using var conn = OpenMeta();

            var (cmsUserId, currentUsername) = GetCurrentUser(httpContext);
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
                Username = username,       // username nhập vào form
                IsSuccess = isSuccess,
                Ip = ip,
                UserAgent = ua,
                Message = message
            });
        }

        public async Task LogActionAsync(HttpContext httpContext, string action, string? entity = null, long? entityId = null, object? details = null)
        {
            using var conn = OpenMeta();

            var (cmsUserId, username) = GetCurrentUser(httpContext);
            var (ip, ua) = GetRequestInfo(httpContext);

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

            const string sql = @"
INSERT INTO dbo.tbl_cms_audit_log(
    cms_user_id,
    username,
    action,
    entity,
    entity_id,
    details_json,
    ip,
    user_agent
) VALUES(
    @CmsUserId,
    @Username,
    @Action,
    @Entity,
    @EntityId,
    @DetailsJson,
    @Ip,
    @UserAgent
);";

            await conn.ExecuteAsync(sql, new
            {
                CmsUserId = cmsUserId,
                Username = username,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                DetailsJson = json,
                Ip = ip,
                UserAgent = ua
            });
        }
    }
}
