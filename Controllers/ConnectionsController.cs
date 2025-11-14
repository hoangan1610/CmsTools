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
    [Authorize]
    public class ConnectionsController : Controller
    {
        private readonly string _metaConn;

        public ConnectionsController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ===== INDEX: danh sách connection =====

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            const string sql = @"
SELECT id          AS Id,
       name        AS Name,
       provider    AS Provider,
       conn_string AS ConnString,
       is_active   AS IsActive
FROM dbo.tbl_cms_connection
ORDER BY name;";

            using var conn = OpenMeta();
            var rows = (await conn.QueryAsync<CmsConnectionEditModel>(sql)).ToList();

            return View(rows); // Views/Connections/Index.cshtml
        }

        // ===== EDIT (GET) – dùng chung cho tạo mới & sửa =====

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                // Tạo mới
                var vmNew = new CmsConnectionEditModel
                {
                    Provider = "mssql",
                    IsActive = true
                };
                return View(vmNew);
            }

            using var conn = OpenMeta();

            const string sql = @"
SELECT id          AS Id,
       name        AS Name,
       provider    AS Provider,
       conn_string AS ConnString,
       is_active   AS IsActive
FROM dbo.tbl_cms_connection
WHERE id = @id;";

            var existing = await conn.QueryFirstOrDefaultAsync<CmsConnectionEditModel>(sql, new { id });
            if (existing == null)
                return NotFound("Connection not found.");

            return View(existing);
        }

        // ===== EDIT (POST) =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CmsConnectionEditModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Tên connection không được để trống.");
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Provider))
            {
                ModelState.AddModelError(nameof(model.Provider), "Provider không được để trống.");
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.ConnString))
            {
                ModelState.AddModelError(nameof(model.ConnString), "Connection string không được để trống.");
                return View(model);
            }

            using var conn = OpenMeta();

            if (model.Id == null || model.Id <= 0)
            {
                // INSERT
                const string insertSql = @"
INSERT INTO dbo.tbl_cms_connection(
    name,
    provider,
    conn_string,
    is_active
) VALUES (
    @Name,
    @Provider,
    @ConnString,
    @IsActive
);
SELECT CAST(SCOPE_IDENTITY() AS int);";

                var newId = await conn.ExecuteScalarAsync<int>(insertSql, new
                {
                    model.Name,
                    model.Provider,
                    model.ConnString,
                    model.IsActive
                });

                TempData["ConnMessage"] = $"Đã tạo connection mới (ID = {newId}).";
            }
            else
            {
                // UPDATE
                const string updateSql = @"
UPDATE dbo.tbl_cms_connection
SET name        = @Name,
    provider    = @Provider,
    conn_string = @ConnString,
    is_active   = @IsActive
WHERE id = @Id;";

                await conn.ExecuteAsync(updateSql, new
                {
                    model.Id,
                    model.Name,
                    model.Provider,
                    model.ConnString,
                    model.IsActive
                });

                TempData["ConnMessage"] = $"Đã cập nhật connection (ID = {model.Id}).";
            }

            return RedirectToAction("Index");
        }

        // (optional) Toggle Active nhanh
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            using var conn = OpenMeta();
            const string sql = @"
UPDATE dbo.tbl_cms_connection
SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END
WHERE id = @id;";

            await conn.ExecuteAsync(sql, new { id });

            TempData["ConnMessage"] = "Đã đổi trạng thái hoạt động của connection.";
            return RedirectToAction("Index");
        }
    }
}
