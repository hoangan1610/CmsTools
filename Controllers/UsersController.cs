using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public class UsersController : Controller
    {
        private readonly string _metaConn;
        private readonly ICmsPasswordHasher _hasher;
        private readonly IAuditLogger _audit;   // 👈 thêm

        public UsersController(IConfiguration cfg, ICmsPasswordHasher hasher, IAuditLogger auditLogger)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");

            _hasher = hasher;
            _audit = auditLogger;               // 👈 gán
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ===================== INDEX =====================

        // ===================== INDEX =====================

        [HttpGet]
        public async Task<IActionResult> Index(int? roleId)
        {
            const string sql = @"
SELECT 
    u.id         AS Id,
    u.username   AS Username,
    u.full_name  AS FullName,
    u.is_active  AS IsActive,
    Roles = ISNULL(
        STUFF(
            (
                SELECT ', ' + r2.name
                FROM dbo.tbl_cms_user_role ur2
                JOIN dbo.tbl_cms_role r2 ON r2.id = ur2.role_id
                WHERE ur2.user_id = u.id
                ORDER BY r2.name
                FOR XML PATH(''), TYPE
            ).value('.', 'nvarchar(max)'),
            1, 2, ''
        ),
        ''
    )
FROM dbo.tbl_cms_user u
WHERE (@roleId IS NULL OR EXISTS (
        SELECT 1
        FROM dbo.tbl_cms_user_role ur
        WHERE ur.user_id = u.id
          AND ur.role_id = @roleId
    ))
ORDER BY u.username;";

            using var conn = OpenMeta();
            var users = (await conn.QueryAsync<CmsUserListItem>(sql, new { roleId })).ToList();

            return View(users); // Views/Users/Index.cshtml  -> @model IEnumerable<CmsUserListItem>
        }


        // ===================== EDIT (GET) =====================

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            using var conn = OpenMeta();

            const string sqlUser = @"
SELECT 
    id         AS Id,
    username   AS Username,
    full_name  AS FullName,
    is_active  AS IsActive
FROM dbo.tbl_cms_user
WHERE id = @id;";

            var user = await conn.QueryFirstOrDefaultAsync<CmsUserEditViewModel>(
                sqlUser,
                new { id });

            if (user == null)
                return NotFound("Không tìm thấy user.");

            const string sqlRoles = @"
SELECT id AS Id, name AS Name
FROM dbo.tbl_cms_role
ORDER BY name;";

            var allRoles = (await conn.QueryAsync<CmsRoleOption>(sqlRoles)).ToList();

            const string sqlUserRoles = @"
SELECT role_id
FROM dbo.tbl_cms_user_role
WHERE user_id = @uid;";

            var selectedRoleIds = (await conn.QueryAsync<int>(
                sqlUserRoles,
                new { uid = id })).ToList();

            user.Roles = allRoles;
            user.SelectedRoleIds = selectedRoleIds;

            return View(user); // Views/Users/Edit.cshtml
        }

        // ===================== EDIT (POST) =====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, IFormCollection form)
        {
            using var conn = OpenMeta();

            var username = await conn.ExecuteScalarAsync<string>(
    "SELECT username FROM dbo.tbl_cms_user WHERE id = @id;",
    new { id });

            // Mở connection trước khi BeginTransaction
            if (conn is SqlConnection sqlConn)
                await sqlConn.OpenAsync();
            else
                conn.Open();

            const string sqlRoles = @"
SELECT id AS Id, name AS Name
FROM dbo.tbl_cms_role
ORDER BY name;";

            var allRoles = (await conn.QueryAsync<CmsRoleOption>(sqlRoles)).ToList();

            string fullName = form["FullName"];
            bool isActive = !string.IsNullOrEmpty(form["IsActive"]);

            var selectedRoleIds = new List<int>();
            var roleValues = form["SelectedRoleIds"]; // name của checkbox role

            foreach (var val in roleValues)
            {
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rid))
                    selectedRoleIds.Add(rid);
            }

            using var tx = conn.BeginTransaction();

            try
            {
                const string sqlUpdateUser = @"
UPDATE dbo.tbl_cms_user
SET full_name = @FullName,
    is_active = @IsActive
WHERE id = @Id;";

                await conn.ExecuteAsync(
                    sqlUpdateUser,
                    new
                    {
                        FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                        IsActive = isActive,
                        Id = id
                    },
                    tx);

                await conn.ExecuteAsync(
                    "DELETE FROM dbo.tbl_cms_user_role WHERE user_id = @uid;",
                    new { uid = id },
                    tx);

                if (selectedRoleIds.Count > 0)
                {
                    const string sqlInsertUserRole = @"
INSERT INTO dbo.tbl_cms_user_role(user_id, role_id)
VALUES (@UserId, @RoleId);";

                    foreach (var rid in selectedRoleIds.Distinct())
                    {
                        await conn.ExecuteAsync(
                            sqlInsertUserRole,
                            new { UserId = id, RoleId = rid },
                            tx);
                    }
                }

                tx.Commit();

                await _audit.LogActionAsync(
    HttpContext,
    action: "cms_user_edit",
    entity: "tbl_cms_user",
    entityId: id,
    details: new
    {
        username,
        fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
        isActive,
        selectedRoleIds = selectedRoleIds.Distinct().ToArray()
    });
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            return RedirectToAction("Index");
        }


        // ===================== CREATE (GET) =====================

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            using var conn = OpenMeta();

            const string sqlRoles = @"
SELECT id AS Id, name AS Name
FROM dbo.tbl_cms_role
ORDER BY name;";

            var allRoles = (await conn.QueryAsync<CmsRoleOption>(sqlRoles)).ToList();

            var vm = new CmsUserEditViewModel
            {
                Roles = allRoles,
                IsActive = true
            };

            return View(vm); // Views/Users/Create.cshtml
        }

        // ===================== CREATE (POST) =====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(IFormCollection form)
        {
            var username = form["Username"].ToString().Trim();
            var fullName = form["FullName"].ToString().Trim();
            var password = form["Password"].ToString();
            var rolesRaw = form["SelectedRoleIds"];

            var roleIds = new List<int>();

            foreach (var val in rolesRaw)
            {
                if (int.TryParse(val, out var rid))
                    roleIds.Add(rid);
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                TempData["UsersMessage"] = "Username và mật khẩu không được để trống.";
                return RedirectToAction("Index");
            }

            using var conn = new SqlConnection(_metaConn);
            await conn.OpenAsync();                    // ✅ BẮT BUỘC: mở connection trước
            using var tran = conn.BeginTransaction();  // ✅ giờ mới BeginTransaction

            try
            {
                // 1) Check trùng username
                var exists = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1) 
              FROM dbo.tbl_cms_user 
              WHERE username = @u;",
                    new { u = username },
                    tran
                );

                if (exists > 0)
                {
                    tran.Rollback();
                    TempData["UsersMessage"] = "Username đã tồn tại.";
                    return RedirectToAction("Index");
                }

                // 2) Hash password (dùng hasher mới)
                var (hash, salt) = _hasher.Hash(password);

                // 3) Insert user
                var userId = await conn.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.tbl_cms_user(
                  username,
                  password_hash,
                  salt,
                  full_name,
                  is_active,
                  created_at_utc
              ) VALUES (
                  @Username,
                  @PasswordHash,
                  @Salt,
                  @FullName,
                  1,
                  SYSUTCDATETIME()
              );
              SELECT CAST(SCOPE_IDENTITY() AS int);",
                    new
                    {
                        Username = username,
                        PasswordHash = hash,
                        Salt = salt,
                        FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName
                    },
                    tran
                );

                // 4) Gán role
                foreach (var rid in roleIds)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO dbo.tbl_cms_user_role(user_id, role_id)
                  VALUES(@UserId, @RoleId);",
                        new { UserId = userId, RoleId = rid },
                        tran
                    );
                }

                tran.Commit();
                TempData["UsersMessage"] = "Đã tạo user mới.";

                await _audit.LogActionAsync(
    HttpContext,
    action: "cms_user_create",
    entity: "tbl_cms_user",
    entityId: userId,
    details: new
    {
        username,
        fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
        roleIds = roleIds.Distinct().ToArray()
    });
                return RedirectToAction("Index");
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }


        // ===================== CHANGE PASSWORD (GET) =====================

        [HttpGet]
        public async Task<IActionResult> ChangePassword(int id)
        {
            using var conn = OpenMeta();

            const string sqlUser = @"
SELECT id, username, full_name, is_active
FROM dbo.tbl_cms_user
WHERE id = @id;";

            var row = await conn.QueryFirstOrDefaultAsync(sqlUser, new { id });
            if (row == null)
                return NotFound("Không tìm thấy user.");

            ViewBag.UserId = (int)row.id;
            ViewBag.Username = (string)row.username;
            ViewData["Title"] = "Đổi mật khẩu - " + (string)row.username;

            return View(); // Views/Users/ChangePassword.cshtml
        }

        // ===================== CHANGE PASSWORD (POST) =====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(int id, IFormCollection form)
        {
            using var conn = OpenMeta();

            string password = form["Password"];
            string confirm = form["ConfirmPassword"];

            if (string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Password không được trống.");
            }
            else if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ModelState.AddModelError("", "Password xác nhận không trùng khớp.");
            }

            const string sqlUser = @"
SELECT id, username
FROM dbo.tbl_cms_user
WHERE id = @id;";

            var row = await conn.QueryFirstOrDefaultAsync(sqlUser, new { id });
            if (row == null)
                return NotFound("Không tìm thấy user.");

            ViewBag.UserId = (int)row.id;
            ViewBag.Username = (string)row.username;
            ViewData["Title"] = "Đổi mật khẩu - " + (string)row.username;

            if (!ModelState.IsValid)
            {
                return View();
            }

            var (hash, salt) = _hasher.Hash(password);

            const string sqlUpdate = @"
UPDATE dbo.tbl_cms_user
SET password_hash = @PasswordHash,
    salt          = @Salt
WHERE id = @Id;";

            await conn.ExecuteAsync(
                sqlUpdate,
                new
                {
                    PasswordHash = hash,
                    Salt = salt,
                    Id = id
                });

            TempData["UsersMessage"] = $"Đã đổi mật khẩu cho user '{(string)row.username}'.";

            // 👇 GHI LOG (không log password)
            await _audit.LogActionAsync(
                HttpContext,
                action: "cms_user_change_password",
                entity: "tbl_cms_user",
                entityId: id,
                details: new
                {
                    username = (string)row.username
                });

            return RedirectToAction("Index");


           
        }

        // ===================== TOGGLE ACTIVE (LOCK/UNLOCK) =====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            using var conn = OpenMeta();

            var current = await conn.ExecuteScalarAsync<bool?>(
                "SELECT is_active FROM dbo.tbl_cms_user WHERE id = @id;",
                new { id });

            if (!current.HasValue)
                return NotFound("User không tồn tại.");

            var newVal = !current.Value;

            await conn.ExecuteAsync(
                "UPDATE dbo.tbl_cms_user SET is_active = @v WHERE id = @id;",
                new { v = newVal, id });

            TempData["UsersMessage"] = newVal
                ? "Đã mở khoá user."
                : "Đã khoá user.";

            await _audit.LogActionAsync(
    HttpContext,
    action: "cms_user_toggle_active",
    entity: "tbl_cms_user",
    entityId: id,
    details: new
    {
        from = current.Value,
        to = newVal
    });

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> LoginLogs(int top = 200)
        {
            using var conn = OpenMeta();

            const string sql = @"
SELECT TOP (@top)
    id              AS Id,
    cms_user_id     AS CmsUserId,
    username        AS Username,
    is_success      AS IsSuccess,
    ip              AS Ip,
    user_agent      AS UserAgent,
    message         AS Message,
    created_at_utc  AS CreatedAtUtc
FROM dbo.tbl_cms_login_log
ORDER BY created_at_utc DESC, id DESC;";

            var rows = (await conn.QueryAsync<CmsLoginLogItem>(
                sql,
                new { top }))
                .ToList();

            return View(rows);  // Views/Users/LoginLogs.cshtml
        }


    }


}
