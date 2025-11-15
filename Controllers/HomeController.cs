using System;
using System.Data;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize] // bắt buộc login
    public class HomeController : Controller
    {
        private readonly string _metaConn;

        public HomeController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private int? GetCurrentCmsUserId()
        {
            var claim = HttpContext.User?.FindFirst("cms_user_id");
            if (claim != null && int.TryParse(claim.Value, out var id))
                return id;

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentCmsUserId();
            if (userId == null)
            {
                // chưa login -> về trang Login
                return RedirectToAction("Login", "Account");
            }

            var isAdmin = User?.HasClaim("cms_is_admin", "1") ?? false;

            await using var conn = new SqlConnection(_metaConn);
            await conn.OpenAsync();

            // 1) Thống kê connection
            const string sqlConnStats = @"
SELECT 
    TotalConnections  = COUNT(1),
    ActiveConnections = SUM(CASE WHEN is_active = 1 THEN 1 ELSE 0 END)
FROM dbo.tbl_cms_connection;";

            var connRow = await conn.QueryFirstAsync(sqlConnStats);
            int totalConnections = (int)connRow.TotalConnections;
            int activeConnections = connRow.ActiveConnections == null
                ? 0
                : (int)connRow.ActiveConnections;

            // 2) Thống kê bảng
            const string sqlTableStats = @"
SELECT 
    TotalTables   = COUNT(1),
    EnabledTables = SUM(CASE WHEN is_enabled = 1 THEN 1 ELSE 0 END)
FROM dbo.tbl_cms_table;";

            var tblRow = await conn.QueryFirstAsync(sqlTableStats);
            int totalTables = (int)tblRow.TotalTables;
            int enabledTables = tblRow.EnabledTables == null
                ? 0
                : (int)tblRow.EnabledTables;

            // 3) Số audit log 24h gần nhất (nếu chưa có bảng audit thì bạn có thể bỏ khối này)
            int auditLast24h = 0;
            try
            {
                const string sqlAudit = @"
SELECT AuditLast24h = COUNT(1)
FROM dbo.tbl_cms_audit_log
WHERE created_at_utc >= DATEADD(day, -1, SYSUTCDATETIME());";

                var auditRow = await conn.QueryFirstAsync(sqlAudit);
                auditLast24h = (int)auditRow.AuditLast24h;
            }
            catch
            {
                // Nếu chưa tạo bảng audit_log thì cứ để 0, không crash
                auditLast24h = 0;
            }

            // 4) Bảng đầu tiên user có can_view = 1
            const string sqlFirstTable = @"
SELECT TOP (1) tp.table_id
FROM dbo.tbl_cms_table_permission tp
JOIN dbo.tbl_cms_table t
    ON t.id = tp.table_id
   AND t.is_enabled = 1
JOIN dbo.tbl_cms_user_role ur
    ON ur.role_id = tp.role_id
WHERE ur.user_id = @uid
  AND tp.can_view = 1
ORDER BY t.display_name, t.table_name;";

            int? firstTableId = await conn.ExecuteScalarAsync<int?>(
                sqlFirstTable,
                new { uid = userId.Value });

            var vm = new HomeDashboardViewModel
            {
                IsAdmin = isAdmin,
                TotalConnections = totalConnections,
                ActiveConnections = activeConnections,
                TotalTables = totalTables,
                EnabledTables = enabledTables,
                AuditLast24h = auditLast24h,
                FirstTableIdForUser = firstTableId
            };

            return View(vm); // Views/Home/Index.cshtml (dashboard)
        }
    }
}
