using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public class SchemaController : Controller
    {
        private readonly string _metaConn;

        public SchemaController(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // ===== 1) INDEX: danh sách bảng theo connection =====

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            const string sql = @"
SELECT 
    t.id                AS Id,
    t.connection_id     AS ConnectionId,
    c.name              AS ConnectionName,
    t.schema_name       AS SchemaName,
    t.table_name        AS TableName,
    t.display_name      AS DisplayName,
    t.primary_key       AS PrimaryKey,
    t.custom_detail_url AS CustomDetailUrl
FROM dbo.tbl_cms_table t
JOIN dbo.tbl_cms_connection c ON c.id = t.connection_id
ORDER BY c.name, t.schema_name, t.table_name;";

            await using var conn = new SqlConnection(_metaConn);
            var rows = (await conn.QueryAsync<CmsTableListItem>(sql)).ToList();

            return View(rows);
        }


        // ===== 2) COLUMNS: cấu hình cột cho 1 bảng =====

        [HttpGet]
        public async Task<IActionResult> Columns(int tableId)
        {
            using var conn = OpenMeta();

            var tableSql = @"
SELECT id,
       connection_id     AS ConnectionId,
       schema_name       AS SchemaName,
       table_name        AS TableName,
       display_name      AS DisplayName,
       primary_key       AS PrimaryKey,
       custom_detail_url AS CustomDetailUrl,
       is_view           AS IsView,
       is_enabled        AS IsEnabled,
       row_filter        AS RowFilter
FROM dbo.tbl_cms_table
WHERE id = @id;";

            var table = await conn.QueryFirstOrDefaultAsync<CmsTableMeta>(tableSql, new { id = tableId });
            if (table == null)
                return NotFound("Table metadata not found.");

            var colSql = @"
SELECT id,
       table_id     AS TableId,
       column_name  AS ColumnName,
       display_name AS DisplayName,
       data_type    AS DataType,
       is_nullable  AS IsNullable,
       is_primary   AS IsPrimary,
       is_list      AS IsList,
       is_editable  AS IsEditable,
       is_filter    AS IsFilter,
       width,
       format,
       sort_order   AS SortOrder,
       default_expr AS DefaultExpr
FROM dbo.tbl_cms_column
WHERE table_id = @tableId
ORDER BY sort_order, column_name;";

            var cols = (await conn.QueryAsync<CmsColumnMeta>(colSql, new { tableId })).ToList();

            var vm = new SchemaColumnsViewModel
            {
                Table = table,
                Columns = cols
            };

            return View(vm); // Views/Schema/Columns.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Columns(int tableId, IFormCollection form)
        {
            using var conn = OpenMeta();

            var tableSql = @"
SELECT id,
       connection_id     AS ConnectionId,
       schema_name       AS SchemaName,
       table_name        AS TableName,
       display_name      AS DisplayName,
       primary_key       AS PrimaryKey,
       custom_detail_url AS CustomDetailUrl,
       is_view           AS IsView,
       is_enabled        AS IsEnabled,
       row_filter        AS RowFilter
FROM dbo.tbl_cms_table
WHERE id = @id;";

            var table = await conn.QueryFirstOrDefaultAsync<CmsTableMeta>(tableSql, new { id = tableId });
            if (table == null)
                return NotFound("Table metadata not found.");

            var colSql = @"
SELECT id,
       table_id     AS TableId,
       column_name  AS ColumnName,
       display_name AS DisplayName,
       data_type    AS DataType,
       is_nullable  AS IsNullable,
       is_primary   AS IsPrimary,
       is_list      AS IsList,
       is_editable  AS IsEditable,
       is_filter    AS IsFilter,
       width,
       format,
       sort_order   AS SortOrder,
       default_expr AS DefaultExpr
FROM dbo.tbl_cms_column
WHERE table_id = @tableId
ORDER BY sort_order, column_name;";

            var cols = (await conn.QueryAsync<CmsColumnMeta>(colSql, new { tableId })).ToList();

            // ===== 2.1) UPDATE META CỦA BẢNG (tbl_cms_table) =====
            var tableDisplayName = form["table_display_name"].ToString();
            var tablePrimaryKey = form["table_primary_key"].ToString();
            var tableDetailUrl = form["table_custom_detail_url"].ToString();

            const string updateTableSql = @"
UPDATE dbo.tbl_cms_table
SET display_name      = @DisplayName,
    primary_key       = @PrimaryKey,
    custom_detail_url = @CustomDetailUrl
WHERE id = @Id;";

            await conn.ExecuteAsync(updateTableSql, new
            {
                Id = tableId,
                DisplayName = string.IsNullOrWhiteSpace(tableDisplayName) ? null : tableDisplayName.Trim(),
                PrimaryKey = string.IsNullOrWhiteSpace(tablePrimaryKey) ? null : tablePrimaryKey.Trim(),
                CustomDetailUrl = string.IsNullOrWhiteSpace(tableDetailUrl) ? null : tableDetailUrl.Trim()
            });

            // ===== 2.2) UPDATE CẤU HÌNH CỘT (tbl_cms_column) =====
            const string updateColSql = @"
UPDATE dbo.tbl_cms_column
SET display_name = @DisplayName,
    is_list      = @IsList,
    is_editable  = @IsEditable,
    is_filter    = @IsFilter,
    width        = @Width,
    format       = @Format,
    default_expr = @DefaultExpr
WHERE id = @Id;";

            foreach (var col in cols)
            {
                var idStr = col.Id.ToString(CultureInfo.InvariantCulture);

                string displayName = form["display_name_" + idStr];
                string format = form["format_" + idStr];
                string defaultExpr = form["default_expr_" + idStr];
                string widthRaw = form["width_" + idStr];

                bool isList = !string.IsNullOrEmpty(form["is_list_" + idStr]);
                bool isEditable = !string.IsNullOrEmpty(form["is_editable_" + idStr]);
                bool isFilter = !string.IsNullOrEmpty(form["is_filter_" + idStr]);

                int? width = null;
                if (int.TryParse(widthRaw, out var w))
                    width = w;

                var param = new
                {
                    Id = col.Id,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    IsList = isList,
                    IsEditable = isEditable,
                    IsFilter = isFilter,
                    Width = width,
                    Format = string.IsNullOrWhiteSpace(format) ? null : format,
                    DefaultExpr = string.IsNullOrWhiteSpace(defaultExpr) ? null : defaultExpr
                };

                await conn.ExecuteAsync(updateColSql, param);
            }

            TempData["SchemaMessage"] = "Đã lưu cấu hình bảng và cột.";

            return RedirectToAction("Columns", new { tableId });
        }

        // ===== 3) SCAN SCHEMA cho 1 connection =====

        private sealed class ConnectionInfo
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
            public string ConnString { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        private sealed class TableInfo
        {
            public string SchemaName { get; set; } = string.Empty;
            public string TableName { get; set; } = string.Empty;
        }

        private sealed class ColumnInfo
        {
            public string COLUMN_NAME { get; set; } = string.Empty;
            public string DATA_TYPE { get; set; } = string.Empty;
            public int? CHARACTER_MAXIMUM_LENGTH { get; set; }
            public byte? NUMERIC_PRECISION { get; set; }
            public int? NUMERIC_SCALE { get; set; }
            public string IS_NULLABLE { get; set; } = "YES";
        }

        private static string BuildDataTypeString(ColumnInfo c)
        {
            var type = c.DATA_TYPE.ToLowerInvariant();

            if (type is "nvarchar" or "varchar" or "char" or "nchar")
            {
                if (c.CHARACTER_MAXIMUM_LENGTH == -1)
                    return type + "(max)";
                if (c.CHARACTER_MAXIMUM_LENGTH.HasValue)
                    return type + "(" + c.CHARACTER_MAXIMUM_LENGTH.Value + ")";
            }

            if (type is "decimal" or "numeric")
            {
                if (c.NUMERIC_PRECISION.HasValue && c.NUMERIC_SCALE.HasValue)
                    return type + "(" + c.NUMERIC_PRECISION.Value + "," + c.NUMERIC_SCALE.Value + ")";
            }

            return type;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Scan(int connectionId)
        {
            using var meta = OpenMeta();

            // 1) Lấy thông tin connection
            const string connSql = @"
SELECT id           AS Id,
       name         AS Name,
       provider     AS Provider,
       conn_string  AS ConnString,
       is_active    AS IsActive
FROM dbo.tbl_cms_connection
WHERE id = @id AND is_active = 1;";

            var connInfo = await meta.QueryFirstOrDefaultAsync<ConnectionInfo>(connSql, new { id = connectionId });
            if (connInfo == null)
            {
                TempData["SchemaMessage"] = "Không tìm thấy connection hoặc đang tắt.";
                return RedirectToAction("Index");
            }

            // 2) Kết nối DB target
            using var target = new SqlConnection(connInfo.ConnString);
            await target.OpenAsync();

            const string tableSql = @"
SELECT TABLE_SCHEMA AS SchemaName,
       TABLE_NAME   AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;";

            var tables = (await target.QueryAsync<TableInfo>(tableSql)).ToList();

            int newTableCount = 0;
            int newColumnCount = 0;

            foreach (var t in tables)
            {
                // 3) Kiểm tra bảng đã có trong tbl_cms_table chưa
                var existingId = await meta.ExecuteScalarAsync<int?>(
                    @"SELECT id
                      FROM dbo.tbl_cms_table
                      WHERE connection_id = @cid
                        AND schema_name   = @schema
                        AND table_name    = @table;",
                    new { cid = connectionId, schema = t.SchemaName, table = t.TableName });

                int tableId = existingId ?? 0;
                if (tableId == 0)
                {
                    tableId = await meta.ExecuteScalarAsync<int>(
                        @"INSERT INTO dbo.tbl_cms_table(
                              connection_id,
                              schema_name,
                              table_name,
                              display_name,
                              primary_key,
                              is_view,
                              is_enabled,
                              sort_order
                          ) VALUES (
                              @cid,
                              @schema,
                              @table,
                              @display,
                              NULL,
                              0,
                              1,
                              100
                          );
                          SELECT CAST(SCOPE_IDENTITY() AS int);",
                        new
                        {
                            cid = connectionId,
                            schema = t.SchemaName,
                            table = t.TableName,
                            display = t.TableName
                        });

                    newTableCount++;
                }

                // 4) Scan cột
                const string colSql = @"
SELECT  COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        NUMERIC_PRECISION,
        NUMERIC_SCALE,
        IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema
  AND TABLE_NAME   = @table
ORDER BY ORDINAL_POSITION;";

                var cols = (await target.QueryAsync<ColumnInfo>(colSql,
                    new { schema = t.SchemaName, table = t.TableName })).ToList();

                int sortOrderBase = 100;

                foreach (var c in cols)
                {
                    var colExistingId = await meta.ExecuteScalarAsync<int?>(
                        @"SELECT id
                          FROM dbo.tbl_cms_column
                          WHERE table_id = @tid
                            AND column_name = @col;",
                        new { tid = tableId, col = c.COLUMN_NAME });

                    if (colExistingId.HasValue)
                        continue; // đã có, không đụng vào (giữ lại cấu hình cũ)

                    string dataType = BuildDataTypeString(c);
                    bool isNullable = string.Equals(c.IS_NULLABLE, "YES", StringComparison.OrdinalIgnoreCase);

                    // Heuristic đơn giản: text dài thì không show ở list
                    bool isList = true;
                    bool isEditable = true;
                    bool isFilter = false;

                    if (dataType.Contains("(max)") ||
                        string.Equals(c.DATA_TYPE, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        isList = false;
                    }

                    await meta.ExecuteAsync(
                        @"INSERT INTO dbo.tbl_cms_column(
                               table_id,
                               column_name,
                               display_name,
                               data_type,
                               is_nullable,
                               is_primary,
                               is_list,
                               is_editable,
                               is_filter,
                               width,
                               format,
                               sort_order,
                               default_expr
                          ) VALUES (
                               @TableId,
                               @ColumnName,
                               @DisplayName,
                               @DataType,
                               @IsNullable,
                               0,
                               @IsList,
                               @IsEditable,
                               @IsFilter,
                               NULL,
                               NULL,
                               @SortOrder,
                               NULL
                          );",
                        new
                        {
                            TableId = tableId,
                            ColumnName = c.COLUMN_NAME,
                            DisplayName = c.COLUMN_NAME,
                            DataType = dataType,
                            IsNullable = isNullable,
                            IsList = isList,
                            IsEditable = isEditable,
                            IsFilter = isFilter,
                            SortOrder = sortOrderBase++
                        });

                    newColumnCount++;
                }
            }

            TempData["SchemaMessage"] =
                $"Scan xong connection '{connInfo.Name}': thêm {newTableCount} bảng, {newColumnCount} cột mới.";

            return RedirectToAction("Index");
        }
    }
}
