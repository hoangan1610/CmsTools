using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
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
            // Lấy cms_user_id từ claim
            int? userId = null;
            var user = HttpContext?.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var claim = user.FindFirst("cms_user_id")
                             ?? user.FindFirst(ClaimTypes.NameIdentifier);

                if (claim != null && int.TryParse(claim.Value, out var uid))
                    userId = uid;
            }

            using var conn = OpenMeta();

            // 1) Lấy danh sách connection active
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

            var connections = (await conn.QueryAsync<CmsConnectionMeta>(sqlConn)).ToList();

            // 2) Xem đã có permission chưa
            const string sqlHasPerm = @"SELECT COUNT(1) FROM dbo.tbl_cms_table_permission;";
            var permCount = await conn.ExecuteScalarAsync<int>(sqlHasPerm);

            string sqlTables;
            object param;

            if (permCount == 0 || userId == null)
            {
                // Chưa config quyền, hoặc chưa đăng nhập -> show tất cả bảng enabled
                sqlTables = @"
SELECT 
    id,
    connection_id     AS ConnectionId,
    schema_name       AS SchemaName,
    table_name        AS TableName,
    display_name      AS DisplayName,
    primary_key       AS PrimaryKey,
    is_view           AS IsView,
    is_enabled        AS IsEnabled,
    row_filter        AS RowFilter,
    custom_detail_url AS CustomDetailUrl
FROM dbo.tbl_cms_table
WHERE is_enabled = 1
ORDER BY connection_id, schema_name, table_name;";

                param = new { };
            }
            else
            {
                // ĐÃ có permission -> chỉ lấy bảng user đó có CanView qua role
                sqlTables = @"
SELECT DISTINCT
    t.id,
    t.connection_id     AS ConnectionId,
    t.schema_name       AS SchemaName,
    t.table_name        AS TableName,
    t.display_name      AS DisplayName,
    t.primary_key       AS PrimaryKey,
    t.is_view           AS IsView,
    t.is_enabled        AS IsEnabled,
    t.row_filter        AS RowFilter,
    t.custom_detail_url AS CustomDetailUrl
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_table_permission tp 
    ON tp.table_id = t.id
   AND tp.can_view = 1
JOIN dbo.tbl_cms_user_role ur
    ON ur.role_id = tp.role_id
WHERE t.is_enabled = 1
  AND ur.user_id = @uid
ORDER BY t.connection_id, t.schema_name, t.table_name;";

                param = new { uid = userId.Value };
            }

            var tables = (await conn.QueryAsync<CmsTableMeta>(sqlTables, param)).ToList();

            // 3) Build tree: connection -> tables
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
                .Where(n => n.Tables.Count > 0) // bỏ connection không có bảng được phép
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
