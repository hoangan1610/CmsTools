using System;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;
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
                // chưa login -> cookie middleware sẽ tự redirect Login,
                // nhưng để chắc chắn:
                return RedirectToAction("Login", "Account");
            }

            var isAdmin = User?.HasClaim("cms_is_admin", "1") ?? false;
            if (isAdmin)
            {
                // Admin: vào trang cấu hình Schema
                return RedirectToAction("Index", "Schema");
            }

            // Non-admin: tìm bảng đầu tiên user có can_view = 1
            await using var conn = new SqlConnection(_metaConn);
            await conn.OpenAsync();

            const string sql = @"
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

            var tableId = await conn.ExecuteScalarAsync<int?>(
                sql,
                new { uid = userId.Value });

            if (tableId.HasValue && tableId.Value > 0)
            {
                // Nhảy sang Data/List cho bảng được phép xem
                return RedirectToAction("List", "Data", new { tableId = tableId.Value });
            }

            // Không có quyền bảng nào -> hiện view thông báo
            return View(); // Views/Home/Index.cshtml
        }
    }
}
