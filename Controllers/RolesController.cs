using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
    public sealed class RolesController : Controller
    {
        private readonly string _metaConn;

        public RolesController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ========== INDEX ==========

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            const string sql = @"
SELECT 
    r.id          AS Id,
    r.name        AS Name,
    r.description AS Description,
    r.is_active   AS IsActive,
    UserCount = (
        SELECT COUNT(1)
        FROM dbo.tbl_cms_user_role ur
        WHERE ur.role_id = r.id
    )
FROM dbo.tbl_cms_role r
ORDER BY r.name;";

            using var conn = OpenMeta();
            var rows = (await conn.QueryAsync<CmsRoleListItem>(sql)).ToList();

            return View(rows);   // Views/Roles/Index.cshtml
        }

        // ========== CREATE (GET) ==========

        [HttpGet]
        public IActionResult Create()
        {
            var vm = new CmsRoleEditViewModel
            {
                IsActive = true
            };

            return View(vm);      // Views/Roles/Create.cshtml
        }

        // ========== CREATE (POST) ==========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CmsRoleEditViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Tên role không được để trống.");
            }

            if (!ModelState.IsValid)
                return View(model);

            using var conn = OpenMeta();

            // check trùng name
            var exists = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1) FROM dbo.tbl_cms_role WHERE name = @Name;",
                new { model.Name });

            if (exists > 0)
            {
                ModelState.AddModelError(nameof(model.Name), "Tên role đã tồn tại.");
                return View(model);
            }

            const string sqlInsert = @"
INSERT INTO dbo.tbl_cms_role(
    name,
    description,
    is_active
) VALUES(
    @Name,
    @Description,
    @IsActive
);";

            await conn.ExecuteAsync(sqlInsert, new
            {
                Name = model.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
                IsActive = model.IsActive
            });

            TempData["RolesMessage"] = "Đã tạo role mới.";
            return RedirectToAction("Index");
        }

        // ========== EDIT (GET) ==========

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            using var conn = OpenMeta();

            const string sql = @"
SELECT 
    id          AS Id,
    name        AS Name,
    description AS Description,
    is_active   AS IsActive
FROM dbo.tbl_cms_role
WHERE id = @id;";

            var row = await conn.QueryFirstOrDefaultAsync<CmsRoleEditViewModel>(sql, new { id });
            if (row == null)
                return NotFound("Không tìm thấy role.");

            return View(row);   // Views/Roles/Edit.cshtml
        }

        // ========== EDIT (POST) ==========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CmsRoleEditViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Tên role không được để trống.");
            }

            if (!ModelState.IsValid)
                return View(model);

            using var conn = OpenMeta();

            // check trùng name (trừ chính nó)
            var exists = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1) 
                  FROM dbo.tbl_cms_role 
                  WHERE name = @Name AND id <> @Id;",
                new { model.Name, Id = id });

            if (exists > 0)
            {
                ModelState.AddModelError(nameof(model.Name), "Tên role đã tồn tại.");
                return View(model);
            }

            const string sqlUpdate = @"
UPDATE dbo.tbl_cms_role
SET name        = @Name,
    description = @Description,
    is_active   = @IsActive
WHERE id = @Id;";

            await conn.ExecuteAsync(sqlUpdate, new
            {
                Id = id,
                Name = model.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
                IsActive = model.IsActive
            });

            TempData["RolesMessage"] = "Đã cập nhật role.";
            return RedirectToAction("Index");
        }

        // (tùy chọn) ========== TOGGLE ACTIVE ==========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool active)
        {
            using var conn = OpenMeta();

            await conn.ExecuteAsync(
                @"UPDATE dbo.tbl_cms_role SET is_active = @Active WHERE id = @Id;",
                new { Active = active, Id = id });

            return RedirectToAction("Index");
        }
    }
}
