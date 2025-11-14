using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.ViewComponents
{
    public sealed class SchemaTreeViewComponent : ViewComponent
    {
        private readonly string _metaConn;

        public SchemaTreeViewComponent(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        public async Task<IViewComponentResult> InvokeAsync(int? currentTableId)
        {
            var user = HttpContext.User;
            var isAdmin = user?.HasClaim("cms_is_admin", "1") ?? false;

            int? userId = null;
            var claim = user?.FindFirst("cms_user_id");
            if (claim != null && int.TryParse(claim.Value, out var uid))
                userId = uid;

            using var conn = OpenMeta();

            List<CmsConnectionMeta> connections;
            List<CmsTableMeta> tables;

            if (isAdmin)
            {
                // ===== Admin: thấy tất cả connection & bảng =====
                const string sqlConn = @"
SELECT 
    id,
    name,
    provider,
    conn_string AS ConnString,
    is_active
FROM dbo.tbl_cms_connection
WHERE is_active = 1
ORDER BY name;";

                const string sqlTable = @"
SELECT 
    id,
    connection_id AS ConnectionId,
    schema_name   AS SchemaName,
    table_name    AS TableName,
    display_name  AS DisplayName,
    primary_key   AS PrimaryKey,
    is_view       AS IsView,
    is_enabled    AS IsEnabled,
    row_filter    AS RowFilter
FROM dbo.tbl_cms_table
WHERE is_enabled = 1
ORDER BY connection_id, schema_name, table_name;";

                connections = (await conn.QueryAsync<CmsConnectionMeta>(sqlConn)).ToList();
                tables = (await conn.QueryAsync<CmsTableMeta>(sqlTable)).ToList();
            }
            else if (userId.HasValue)
            {
                // ===== User thường: chỉ thấy các bảng có can_view = 1 theo role =====

                const string sqlConnUser = @"
SELECT DISTINCT
    c.id,
    c.name,
    c.provider,
    c.conn_string AS ConnString,
    c.is_active
FROM dbo.tbl_cms_connection c
JOIN dbo.tbl_cms_table t 
    ON t.connection_id = c.id
   AND t.is_enabled = 1
JOIN dbo.tbl_cms_table_permission tp 
    ON tp.table_id = t.id
   AND tp.can_view = 1
JOIN dbo.tbl_cms_user_role ur
    ON ur.role_id = tp.role_id
WHERE c.is_active = 1
  AND ur.user_id = @uid
ORDER BY c.name;";

                const string sqlTableUser = @"
SELECT DISTINCT
    t.id,
    t.connection_id AS ConnectionId,
    t.schema_name   AS SchemaName,
    t.table_name    AS TableName,
    t.display_name  AS DisplayName,
    t.primary_key   AS PrimaryKey,
    t.is_view       AS IsView,
    t.is_enabled    AS IsEnabled,
    t.row_filter    AS RowFilter
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_table_permission tp 
    ON tp.table_id = t.id
   AND tp.can_view = 1
JOIN dbo.tbl_cms_user_role ur
    ON ur.role_id = tp.role_id
WHERE t.is_enabled = 1
  AND ur.user_id = @uid
ORDER BY t.connection_id, t.schema_name, t.table_name;";

                connections = (await conn.QueryAsync<CmsConnectionMeta>(
                    sqlConnUser, new { uid = userId.Value })
                ).ToList();

                tables = (await conn.QueryAsync<CmsTableMeta>(
                    sqlTableUser, new { uid = userId.Value })
                ).ToList();
            }
            else
            {
                // Không login / không có claim user -> không có gì để hiển thị
                connections = new List<CmsConnectionMeta>();
                tables = new List<CmsTableMeta>();
            }

            // Build tree: mỗi connection -> list bảng thuộc connection đó
            var nodes = connections
                .Select(c => new SchemaTreeViewModel.ConnectionNode
                {
                    Connection = c,
                    Tables = tables
                        .Where(t => t.ConnectionId == c.Id)
                        .OrderBy(t => t.SchemaName)
                        .ThenBy(t => t.TableName)
                        .ToList()
                })
                // Bỏ các connection không có bảng nào
                .Where(n => n.Tables.Count > 0)
                .ToList();

            var vm = new SchemaTreeViewModel
            {
                CurrentTableId = currentTableId,
                Items = nodes
            };

            return View(vm); // Views/Shared/Components/SchemaTree/Default.cshtml
        }
    }
}
