using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class AuditController : Controller
    {
        private readonly string _metaConn;

        public AuditController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ========== LIST ==========

        [HttpGet]
        public async Task<IActionResult> Index(
            string? op,
            string? table,
            string? from,
            string? to,
            int page = 1,
            int pageSize = 100)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 500) pageSize = 100;

            using var conn = OpenMeta();

            var whereParts = new List<string>();
            var param = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(op))
            {
                whereParts.Add("l.operation = @op");
                param.Add("op", op.Trim());
            }

            if (!string.IsNullOrWhiteSpace(table))
            {
                whereParts.Add("(l.table_name LIKE @tbl OR (l.schema_name + '.' + l.table_name) LIKE @tbl)");
                param.Add("tbl", "%" + table.Trim() + "%");
            }

            if (DateTime.TryParse(from, out var fromDt))
            {
                whereParts.Add("l.created_at_utc >= @fromUtc");
                param.Add("fromUtc", fromDt.ToUniversalTime());
            }

            if (DateTime.TryParse(to, out var toDt))
            {
                whereParts.Add("l.created_at_utc <= @toUtc");
                param.Add("toUtc", toDt.ToUniversalTime());
            }

            var whereSql = whereParts.Count > 0
                ? "WHERE " + string.Join(" AND ", whereParts)
                : string.Empty;

            var sql = $@"
SELECT COUNT(*)
FROM dbo.tbl_cms_audit_log l
{whereSql};

SELECT 
    l.id                 AS Id,
    l.user_id            AS UserId,
    u.username           AS Username,
    l.operation          AS Operation,
    l.connection_name    AS ConnectionName,
    l.schema_name        AS SchemaName,
    l.table_name         AS TableName,
    l.primary_key_column AS PrimaryKeyColumn,
    l.primary_key_value  AS PrimaryKeyValue,
    l.ip_address         AS IpAddress,
    l.created_at_utc     AS CreatedAtUtc
FROM dbo.tbl_cms_audit_log l
LEFT JOIN dbo.tbl_cms_user u ON u.id = l.user_id
{whereSql}
ORDER BY l.id DESC
OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY;";

            param.Add("offset", (page - 1) * pageSize);
            param.Add("limit", pageSize);

            using var multi = await conn.QueryMultipleAsync(sql, param);
            var total = await multi.ReadFirstAsync<long>();
            var rows = (await multi.ReadAsync<CmsAuditLogListItem>()).ToList();

            var vm = new CmsAuditLogListViewModel
            {
                Items = rows,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Operation = op,
                TableFilter = table,
                From = from,
                To = to
            };

            return View(vm); // Views/Audit/Index.cshtml
        }

        // ========== DETAILS ==========

        [HttpGet]
        public async Task<IActionResult> Details(long id)
        {
            using var conn = OpenMeta();

            const string sql = @"
SELECT 
    l.id                 AS Id,
    l.user_id            AS UserId,
    u.username           AS Username,
    l.operation          AS Operation,
    l.connection_name    AS ConnectionName,
    l.schema_name        AS SchemaName,
    l.table_name         AS TableName,
    l.primary_key_column AS PrimaryKeyColumn,
    l.primary_key_value  AS PrimaryKeyValue,
    l.ip_address         AS IpAddress,
    l.user_agent         AS UserAgent,
    l.created_at_utc     AS CreatedAtUtc,
    l.old_values         AS OldValuesJson,
    l.new_values         AS NewValuesJson
FROM dbo.tbl_cms_audit_log l
LEFT JOIN dbo.tbl_cms_user u ON u.id = l.user_id
WHERE l.id = @id;";

            var row = await conn.QueryFirstOrDefaultAsync<CmsAuditLogDetailModel>(sql, new { id });
            if (row == null)
                return NotFound("Audit log not found.");

            return View(row); // Views/Audit/Details.cshtml
        }
    }
}
