// Services/CmsAuditLogger.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Services
{
    public sealed class CmsAuditLogger : ICmsAuditLogger
    {
        private readonly string _metaConn;
        private readonly IHttpContextAccessor _http;

        public CmsAuditLogger(IConfiguration cfg, IHttpContextAccessor http)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");

            _http = http;
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        public async Task LogAsync(
    int? userId,
    string operation,
    CmsTableMeta table,
    string pkColumn,
    object pkValue,
    IReadOnlyDictionary<string, object?>? oldValues,
    IReadOnlyDictionary<string, object?>? newValues)
        {
            try
            {
                using var conn = OpenMeta();

                // Lấy tên connection từ metadata
                var connName = await conn.ExecuteScalarAsync<string?>(
                    "SELECT name FROM dbo.tbl_cms_connection WHERE id = @id;",
                    new { id = table.ConnectionId });

                if (string.IsNullOrWhiteSpace(connName))
                    connName = table.ConnectionId.ToString(CultureInfo.InvariantCulture);

                // Lấy IP + UserAgent từ HttpContext (nếu có)
                var http = _http.HttpContext;
                string? ip = null;
                string? userAgent = null;

                if (http != null)
                {
                    ip = http.Connection.RemoteIpAddress?.ToString();
                    userAgent = http.Request.Headers["User-Agent"].ToString();
                }

                string? oldJson = null;
                string? newJson = null;

                if (oldValues != null && oldValues.Count > 0)
                    oldJson = JsonSerializer.Serialize(oldValues);

                if (newValues != null && newValues.Count > 0)
                    newJson = JsonSerializer.Serialize(newValues);

                var pkValueStr = pkValue?.ToString();

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
      new_values,
      created_at_utc
) VALUES (
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
      @NewValues,
      SYSUTCDATETIME()
);";

                await conn.ExecuteAsync(sql, new
                {
                    UserId = userId,
                    Operation = operation,
                    ConnectionName = connName,
                    SchemaName = table.SchemaName,
                    TableName = table.TableName,
                    PrimaryKeyColumn = pkColumn,
                    PrimaryKeyValue = pkValueStr,
                    IpAddress = ip,
                    UserAgent = userAgent,
                    OldValues = oldJson,
                    NewValues = newJson
                });
            }
            catch
            {
                // Không cho lỗi log làm hư flow chính
                // Có thể TODO: ghi lỗi ra file / Serilog sau
            }
        }
    }
}
