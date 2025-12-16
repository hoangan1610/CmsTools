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
    public class PermissionsController : Controller
    {
        private readonly string _metaConn;

        public PermissionsController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ===== 1) INDEX: danh sách role + số user + số bảng có permission =====
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            const string sql = @"
SELECT 
    r.id          AS Id,
    r.name        AS Name,
    r.description AS Description,
    UserCount = (
        SELECT COUNT(*)
        FROM dbo.tbl_cms_user_role ur
        WHERE ur.role_id = r.id
    ),
    TablePermCount = (
        SELECT COUNT(*)
        FROM dbo.tbl_cms_table_permission tp
        WHERE tp.role_id = r.id
    )
FROM dbo.tbl_cms_role r
ORDER BY r.name;";

            using var conn = OpenMeta();
            var list = (await conn.QueryAsync<RoleListItem>(sql)).ToList();
            return View(list);
        }

        // ===== 2) EDIT (GET): sửa permission của 1 role =====
        [HttpGet]
        public async Task<IActionResult> Edit(int roleId)
        {
            var vm = await BuildPermissionViewModelAsync(roleId, permSourceRoleId: null);
            if (vm == null)
                return NotFound("Role không tồn tại.");

            return View(vm);
        }

        private async Task<TablePermissionEditViewModel?> BuildPermissionViewModelAsync(int roleId, int? permSourceRoleId)
        {
            using var conn = OpenMeta();

            const string roleSql = @"
SELECT id, name, description
FROM dbo.tbl_cms_role
WHERE id = @id;";

            var role = await conn.QueryFirstOrDefaultAsync(roleSql, new { id = roleId });
            if (role == null) return null;

            // Danh sách role khác để hiển thị trong dropdown copy
            const string rolesSql = @"
SELECT id AS Id, name AS Name
FROM dbo.tbl_cms_role
WHERE id <> @roleId
ORDER BY name;";

            var copyRoles = (await conn.QueryAsync<CmsRoleOption>(rolesSql, new { roleId }))
                .ToList();

            var permRoleId = permSourceRoleId ?? roleId;

            // ✅ FIX: thiếu dấu phẩy + sai vị trí RowFilter + dư dấu phẩy trước FROM
            const string tableSql = @"
SELECT 
    t.id                  AS TableId,
    t.schema_name         AS SchemaName,
    t.table_name          AS TableName,
    t.display_name        AS DisplayName,
    c.name                AS ConnectionName,

    ISNULL(tp.can_view,     0) AS CanView,
    ISNULL(tp.can_create,   0) AS CanCreate,
    ISNULL(tp.can_update,   0) AS CanUpdate,
    ISNULL(tp.can_delete,   0) AS CanDelete,

    ISNULL(tp.can_publish,  0) AS CanPublish,
    ISNULL(tp.can_schedule, 0) AS CanSchedule,
    ISNULL(tp.can_archive,  0) AS CanArchive,

    tp.row_filter           AS RowFilter
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_connection c
    ON t.connection_id = c.id
LEFT JOIN dbo.tbl_cms_table_permission tp
    ON tp.table_id = t.id AND tp.role_id = @permRoleId
WHERE t.is_enabled = 1
ORDER BY c.name, t.schema_name, t.table_name;";

            var rows = await conn.QueryAsync(tableSql, new { permRoleId });

            var items = rows.Select(r => new TablePermissionItem
            {
                TableId = r.TableId,
                TableName = $"{(string)r.SchemaName}.{(string)r.TableName}",
                DisplayName = string.IsNullOrWhiteSpace((string?)r.DisplayName)
                    ? $"{(string)r.SchemaName}.{(string)r.TableName}"
                    : (string)r.DisplayName,
                ConnectionName = r.ConnectionName,

                CanView = (bool)r.CanView,
                CanCreate = (bool)r.CanCreate,
                CanUpdate = (bool)r.CanUpdate,
                CanDelete = (bool)r.CanDelete,

                CanPublish = (bool)r.CanPublish,
                CanSchedule = (bool)r.CanSchedule,
                CanArchive = (bool)r.CanArchive,

                RowFilter = r.RowFilter
            }).ToList();

            return new TablePermissionEditViewModel
            {
                RoleId = roleId,
                RoleName = role.name,
                Items = items,
                CopyRoles = copyRoles,
                CopyFromRoleId = permSourceRoleId
            };
        }

        // ===== 3) EDIT (POST): lưu permission cho role =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TablePermissionEditViewModel model)
        {
            if (model.RoleId <= 0)
                return BadRequest("RoleId không hợp lệ.");

            using var conn = OpenMeta();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                const string deleteSql = @"
DELETE FROM dbo.tbl_cms_table_permission
WHERE role_id = @roleId;";

                await conn.ExecuteAsync(deleteSql, new { roleId = model.RoleId }, tx);

                if (model.Items != null && model.Items.Count > 0)
                {
                    // ✅ thêm 3 cột vào insert
                    const string insertSql = @"
INSERT INTO dbo.tbl_cms_table_permission(
    table_id,
    role_id,
    can_view,
    can_create,
    can_update,
    can_delete,
    can_publish,
    can_schedule,
    can_archive,
    row_filter
)
VALUES (
    @TableId,
    @RoleId,
    @CanView,
    @CanCreate,
    @CanUpdate,
    @CanDelete,
    @CanPublish,
    @CanSchedule,
    @CanArchive,
    @RowFilter
);";

                    foreach (var item in model.Items)
                    {
                        // ✅ hasAny phải tính luôn Publish/Schedule/Archive
                        var hasAny =
                            item.CanView || item.CanCreate || item.CanUpdate || item.CanDelete
                            || item.CanPublish || item.CanSchedule || item.CanArchive
                            || !string.IsNullOrWhiteSpace(item.RowFilter);

                        if (!hasAny) continue;

                        await conn.ExecuteAsync(insertSql, new
                        {
                            TableId = item.TableId,
                            RoleId = model.RoleId,

                            CanView = item.CanView,
                            CanCreate = item.CanCreate,
                            CanUpdate = item.CanUpdate,
                            CanDelete = item.CanDelete,

                            CanPublish = item.CanPublish,
                            CanSchedule = item.CanSchedule,
                            CanArchive = item.CanArchive,

                            RowFilter = string.IsNullOrWhiteSpace(item.RowFilter) ? null : item.RowFilter
                        }, tx);
                    }
                }

                tx.Commit();

                TempData["PermMessage"] = "Đã lưu quyền cho role.";
                return RedirectToAction("Index");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ===== COPY QUYỀN TỪ ROLE KHÁC (chỉ load form, chưa lưu DB) =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyFrom(int roleId, int? fromRoleId)
        {
            if (roleId <= 0)
                return BadRequest("RoleId không hợp lệ.");

            if (fromRoleId == null || fromRoleId <= 0)
                return RedirectToAction("Edit", new { roleId });

            var vm = await BuildPermissionViewModelAsync(roleId, permSourceRoleId: fromRoleId);
            if (vm == null)
                return NotFound("Role không tồn tại.");

            TempData["PermMessage"] = $"Đã nạp quyền từ role ID {fromRoleId}. Nhớ bấm Lưu để ghi xuống DB.";
            return View("Edit", vm);
        }

        // ===== INDEX THEO BẢNG: filter + highlight bảng "mồ côi" =====
        [HttpGet]
        public async Task<IActionResult> Tables(int? connectionId, string? q)
        {
            using var conn = OpenMeta();

            const string sqlConn = @"
SELECT id, name, provider, conn_string AS ConnString, is_active
FROM dbo.tbl_cms_connection
ORDER BY name;";

            var connections = (await conn.QueryAsync<CmsConnectionMeta>(sqlConn)).ToList();

            const string sql = @"
SELECT 
    t.id              AS TableId,
    c.name            AS ConnectionName,
    t.schema_name     AS SchemaName,
    t.table_name      AS TableName,
    t.display_name    AS DisplayName,
    HasViewRole = CASE 
        WHEN EXISTS (
            SELECT 1 
            FROM dbo.tbl_cms_table_permission tp
            WHERE tp.table_id = t.id
              AND tp.can_view = 1
        ) THEN CAST(1 AS bit)
        ELSE CAST(0 AS bit)
    END
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_connection c ON c.id = t.connection_id
WHERE t.is_enabled = 1
  AND c.is_active = 1
  AND (@connId IS NULL OR t.connection_id = @connId)
  AND (
        @q IS NULL OR @q = '' OR
        t.table_name LIKE '%' + @q + '%' OR
        t.schema_name LIKE '%' + @q + '%' OR
        t.display_name LIKE '%' + @q + '%' OR
        c.name LIKE '%' + @q + '%'
      )
ORDER BY c.name, t.schema_name, t.table_name;";

            var items = (await conn.QueryAsync<PermissionTableListItem>(sql, new { connId = connectionId, q }))
                .ToList();

            var vm = new PermissionTableIndexViewModel
            {
                Items = items,
                Connections = connections,
                ConnectionId = connectionId,
                Search = q
            };

            return View(vm);
        }
    }
}
