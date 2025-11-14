using System.Text;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using CmsTools.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.ObjectModel;

namespace CmsTools.Controllers
{
    [Authorize]
    public class DataController : Controller
    {
        private readonly ICmsMetaService _meta;
        private readonly ICmsDbRouter _router;
        private readonly ICmsPermissionService _perms;
        private readonly ICmsAuditLogger _audit;

        public DataController(
    ICmsMetaService meta,
    ICmsDbRouter router,
    ICmsPermissionService perms,
    ICmsAuditLogger audit)        // 👈 thêm param
        {
            _meta = meta;
            _router = router;
            _perms = perms;
            _audit = audit;               // 👈 gán
        }

        // ========== LIST ==========

        [HttpGet]
        public async Task<IActionResult> List(int tableId, int page = 1, int pageSize = 50)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView)
                return Forbid();

            // Lấy full metadata để tách ListColumns và FilterColumns
            var allCols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!allCols.Any())
                return BadRequest("No columns configured.");

            var listCols = allCols.Where(c => c.IsList).ToList();
            if (!listCols.Any())
            {
                // fallback: nếu chưa gắn is_list, lấy hết
                listCols = allCols.ToList();
            }

            var filterCols = allCols.Where(c => c.IsFilter).ToList();

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            // ========== Đọc filter từ QueryString ==========
            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var filterSqlParts = new List<string>();
            var filterParams = new Dictionary<string, object?>();

            foreach (var col in filterCols)
            {
                var raw = Request.Query[col.ColumnName].ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                filters[col.ColumnName] = raw;

                var paramName = "f_" + col.ColumnName;
                var dt = (col.DataType ?? "").ToLowerInvariant();

                if (dt.Contains("char") || dt.Contains("text"))
                {
                    // Lọc LIKE cho dạng chuỗi
                    filterSqlParts.Add($"[{col.ColumnName}] LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw + "%";
                }
                else if (dt.StartsWith("tinyint") || dt.StartsWith("int") ||
                         dt.StartsWith("bigint") || dt.StartsWith("smallint"))
                {
                    // Lọc chính xác cho dạng số
                    if (long.TryParse(raw, out var num))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = num;
                    }
                }
                else
                {
                    // Fallback: cast sang nvarchar rồi LIKE
                    filterSqlParts.Add($"CAST([{col.ColumnName}] AS nvarchar(255)) LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw + "%";
                }
            }

            // where = row_filter (bảng) + row_filter (permission) + filter từ user
            string? where = table.RowFilter;

            if (!string.IsNullOrWhiteSpace(perm.RowFilter))
            {
                if (string.IsNullOrWhiteSpace(where))
                    where = perm.RowFilter;
                else
                    where = "(" + where + ") AND (" + perm.RowFilter + ")";
            }

            if (filterSqlParts.Any())
            {
                var userWhere = string.Join(" AND ", filterSqlParts);
                if (string.IsNullOrWhiteSpace(where))
                    where = userWhere;
                else
                    where = "(" + where + ") AND (" + userWhere + ")";
            }

            var result = await _router.QueryAsync(
                connMeta,
                table,
                listCols,
                where,
                filterParams,
                page,
                pageSize);

            var vm = new DataListViewModel
            {
                Table = table,
                Columns = listCols,
                FilterColumns = filterCols,
                Rows = result.Rows,
                Total = result.Total,
                Page = page,
                PageSize = pageSize,
                Filters = filters,
                Permission = perm
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv(int tableId, int pageSize = 50000)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView)
                return Forbid();

            var allCols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!allCols.Any())
                return BadRequest("No columns configured.");

            var listCols = allCols.Where(c => c.IsList).ToList();
            if (!listCols.Any())
                listCols = allCols.ToList();

            var filterCols = allCols.Where(c => c.IsFilter).ToList();

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            // ==== build filter y như List() ====
            var filterSqlParts = new List<string>();
            var filterParams = new Dictionary<string, object?>();

            foreach (var col in filterCols)
            {
                var raw = Request.Query[col.ColumnName].ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var paramName = "f_" + col.ColumnName;
                var dt = (col.DataType ?? "").ToLowerInvariant();

                if (dt.Contains("char") || dt.Contains("text"))
                {
                    filterSqlParts.Add($"[{col.ColumnName}] LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw + "%";
                }
                else if (dt.StartsWith("tinyint") || dt.StartsWith("int") ||
                         dt.StartsWith("bigint") || dt.StartsWith("smallint"))
                {
                    if (long.TryParse(raw, out var num))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = num;
                    }
                }
                else
                {
                    filterSqlParts.Add($"CAST([{col.ColumnName}] AS nvarchar(255)) LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw + "%";
                }
            }

            string? where = table.RowFilter;

            if (!string.IsNullOrWhiteSpace(perm.RowFilter))
            {
                if (string.IsNullOrWhiteSpace(where))
                    where = perm.RowFilter;
                else
                    where = "(" + where + ") AND (" + perm.RowFilter + ")";
            }

            if (filterSqlParts.Any())
            {
                var userWhere = string.Join(" AND ", filterSqlParts);
                if (string.IsNullOrWhiteSpace(where))
                    where = userWhere;
                else
                    where = "(" + where + ") AND (" + userWhere + ")";
            }

            // Lấy tối đa pageSize bản ghi (default 50k)
            var result = await _router.QueryAsync(
                connMeta,
                table,
                listCols,
                where,
                filterParams,
                page: 1,
                pageSize: pageSize);

            // ==== build CSV ====
            var sb = new StringBuilder();

            // header
            for (int i = 0; i < listCols.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeCsv(listCols[i].DisplayName ?? listCols[i].ColumnName));
            }
            sb.AppendLine();

            // rows
            foreach (var row in result.Rows)
            {
                for (int i = 0; i < listCols.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var col = listCols[i];

                    object? val = null;
                    if (row != null && row.ContainsKey(col.ColumnName))
                        val = row[col.ColumnName];

                    sb.Append(EscapeCsv(val?.ToString() ?? string.Empty));
                }
                sb.AppendLine();
            }

            var fileName = $"{table.TableName}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // Helper CSV
        private static string EscapeCsv(string s)
        {
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            {
                s = s.Replace("\"", "\"\"");
                return $"\"{s}\"";
            }
            return s;
        }


        // ========== EDIT (GET) ==========

        [HttpGet]
        public async Task<IActionResult> Edit(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate && !perm.CanView)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol))
                return BadRequest("Primary key not configured.");

            var row = await _router.GetRowAsync(
                connMeta,
                table,
                cols,
                pkCol,
                id);

            if (row == null)
                return NotFound("Row not found.");

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            return View(vm);  // Views/Data/Edit.cshtml
        }



        // ========== EDIT (POST) ==========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int tableId, long id, IFormCollection form)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol))
                return BadRequest("Primary key not configured.");

            // ==== lấy snapshot cũ cho audit ====
            var oldRow = await _router.GetRowAsync(
                connMeta,
                table,
                cols,
                pkCol,
                id);

            IReadOnlyDictionary<string, object?>? oldSnapshot = null;
            if (oldRow != null)
            {
                oldSnapshot = new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase));
            }

            // chỉ update các cột cho phép
            // chỉ update các cột cho phép, và KHÔNG bao giờ đụng cột PK
            var editableCols = cols
                .Where(c =>
                    c.IsEditable
                    && !c.IsPrimary
                    && !c.ColumnName.Equals(pkCol, StringComparison.OrdinalIgnoreCase))
                .ToList();


            var values = new Dictionary<string, object?>();

            foreach (var col in editableCols)
            {
                var raw = form[col.ColumnName].ToString();
                values[col.ColumnName] = ConvertRawToType(raw, col);
            }

            // ==== chuẩn bị newRow cho audit ====
            IReadOnlyDictionary<string, object?>? newSnapshot = null;
            if (oldRow != null)
            {
                var newRowDict = new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in values)
                {
                    newRowDict[kv.Key] = kv.Value;
                }

                newSnapshot = new ReadOnlyDictionary<string, object?>(newRowDict);
            }

            var affected = await _router.UpdateRowAsync(
                connMeta,
                table,
                editableCols,
                pkCol,
                id,
                values);

            if (affected <= 0)
            {
                return BadRequest("Update failed.");
            }

            // ==== ghi log audit ====
            await _audit.LogAsync(
                userId,
                "UPDATE",
                table,
                pkCol,
                id,
                oldSnapshot,
                newSnapshot);

            // sau khi lưu xong quay lại List
            return RedirectToAction("List", new { tableId });
        }


        // ========== CREATE (GET) ==========

        [HttpGet]
        public async Task<IActionResult> Create(int tableId)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanCreate)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            var vm = new DataCreateViewModel
            {
                Table = table,
                Columns = cols
            };

            return View(vm); // Views/Data/Create.cshtml
        }

        // ========== CREATE (POST) ==========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int tableId, IFormCollection form)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanCreate)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            // === tìm PK để log ===
            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol))
                return BadRequest("Primary key not configured.");

            // 1) Cột cho phép user nhập tay
            var editableFromForm = cols
                .Where(c => c.IsEditable && !c.IsPrimary)
                .ToList();

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Lấy giá trị từ form
            foreach (var col in editableFromForm)
            {
                var raw = form[col.ColumnName].ToString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    values[col.ColumnName] = ConvertRawToType(raw, col);
                }
            }

            // 2) Áp dụng default_expr cho những cột chưa có giá trị
            var finalCols = new List<CmsColumnMeta>(editableFromForm);

            foreach (var col in cols)
            {
                if (col.IsPrimary)
                    continue;

                var hasValue = values.ContainsKey(col.ColumnName);
                var hasDefault = !string.IsNullOrWhiteSpace(col.DefaultExpr);

                if (!hasDefault)
                    continue;

                if (!hasValue)
                {
                    var defVal = EvaluateDefaultExpr(col.DefaultExpr!, col);
                    values[col.ColumnName] = defVal;

                    if (!finalCols.Any(c => c.ColumnName.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        finalCols.Add(col);
                    }
                }
            }

            // Gọi Insert với danh sách cột cuối cùng (form + default_expr)
            var newId = await _router.InsertRowAsync(connMeta, table, finalCols, values);
            if (newId <= 0)
            {
                return BadRequest("Insert failed.");
            }

            // ==== build newRow để audit ====
            var newRowDict = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase)
            {
                [pkCol] = newId
            };

            IReadOnlyDictionary<string, object?> newSnapshot =
                new ReadOnlyDictionary<string, object?>(newRowDict);

            await _audit.LogAsync(
                userId,
                "CREATE",
                table,
                pkCol,
                newId,
                null,        // oldValues
                newSnapshot  // newValues
            );

            return RedirectToAction("List", new { tableId });
        }


        // ========== SET STATUS (Ẩn/Kích hoạt) ==========

        private CmsColumnMeta? FindStatusColumn(IReadOnlyList<CmsColumnMeta> cols)
        {
            return cols.FirstOrDefault(c =>
                string.Equals(c.ColumnName, "status", StringComparison.OrdinalIgnoreCase));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(int tableId, long id, byte status)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate && !perm.CanDelete)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol))
                return BadRequest("Primary key not configured.");

            var statusCol = FindStatusColumn(cols);
            if (statusCol == null)
                return BadRequest("Status column not found.");

            var editableCols = new List<CmsColumnMeta> { statusCol };
            var values = new Dictionary<string, object?>
            {
                [statusCol.ColumnName] = status
            };

            // ==== snapshot cũ ====
            var oldRow = await _router.GetRowAsync(
                connMeta,
                table,
                cols,
                pkCol,
                id);

            IReadOnlyDictionary<string, object?>? oldSnapshot = null;
            IReadOnlyDictionary<string, object?>? newSnapshot = null;

            if (oldRow != null)
            {
                oldSnapshot = new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase));

                var newRowDict = new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase)
                {
                    [statusCol.ColumnName] = status
                };

                newSnapshot = new ReadOnlyDictionary<string, object?>(newRowDict);
            }

            var affected = await _router.UpdateRowAsync(
                connMeta,
                table,
                editableCols,
                pkCol,
                id,
                values);

            if (affected <= 0)
                return BadRequest("Update status failed.");

            // ==== ghi log ====
            await _audit.LogAsync(
                userId,
                "SET_STATUS",
                table,
                pkCol,
                id,
                oldSnapshot,
                newSnapshot);

            return RedirectToAction("List", new { tableId });
        }



        [HttpGet]
        public async Task<IActionResult> Details(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null)
                return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null)
                return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView)
                return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any())
                return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive)
                return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol))
                return BadRequest("Primary key not configured.");


            

            // lấy dòng dữ liệu
            var row = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);
            if (row == null)
                return NotFound("Row not found.");

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            return View(vm); // Views/Data/Details.cshtml
        }


        // ========== Helper ==========

        private int? GetCurrentCmsUserId()
        {
            var claim = HttpContext.User?.FindFirst("cms_user_id");
            if (claim != null && int.TryParse(claim.Value, out var id))
                return id;

            return null;
        }

        // Helper: convert string từ form sang kiểu tương ứng
        private static object? ConvertRawToType(string raw, CmsColumnMeta col)
        {
            var t = (col.DataType ?? string.Empty).ToLowerInvariant();

            // ===== BIT (checkbox) =====
            // - Edit: checkbox không tick => raw = ""  -> false
            // - Edit: checkbox tick     => raw = "on"/"1"/"true" -> true
            // - Create: mình chỉ gọi ConvertRawToType nếu raw != "" (đã code ở Create),
            //   nên default_expr vẫn hoạt động bình thường.
            if (t.StartsWith("bit"))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                raw = raw.Trim();
                if (raw.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    raw == "1")
                    return true;

                return false;
            }

            // ===== CÁC KIỂU KHÁC =====
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (col.IsNullable)
                    return null;

                // không nullable mà rỗng -> để DB xử lý, mình trả null
                return null;
            }

            try
            {
                if (t.StartsWith("bigint"))
                    return long.Parse(raw, CultureInfo.InvariantCulture);

                if (t.StartsWith("int") || t.StartsWith("smallint") || t.StartsWith("tinyint"))
                    return int.Parse(raw, CultureInfo.InvariantCulture);

                if (t.StartsWith("decimal") || t.StartsWith("numeric") ||
                    t.StartsWith("money") || t.StartsWith("smallmoney"))
                    return decimal.Parse(raw, CultureInfo.InvariantCulture);

                if (t.StartsWith("float") || t.StartsWith("real"))
                    return double.Parse(raw, CultureInfo.InvariantCulture);

                if (t.StartsWith("datetime") || t.StartsWith("date") || t.StartsWith("smalldatetime"))
                    return DateTime.Parse(raw, CultureInfo.CurrentCulture);

                // nvarchar, varchar, text...
                return raw;
            }
            catch
            {
                // parse lỗi thì trả lại string, để DB tự xử lý
                return raw;
            }
        }

        private static string? ResolvePrimaryKey(CmsTableMeta table, IReadOnlyList<CmsColumnMeta> cols)
        {
            // 1) Ưu tiên cấu hình primary_key ở tbl_cms_table
            if (!string.IsNullOrWhiteSpace(table.PrimaryKey))
                return table.PrimaryKey;

            // 2) Nếu có cột được đánh IsPrimary
            var pk = cols.FirstOrDefault(c => c.IsPrimary)?.ColumnName;
            if (!string.IsNullOrWhiteSpace(pk))
                return pk;

            // 3) Fallback: cột tên "id"
            var idCol = cols.FirstOrDefault(c =>
                c.ColumnName.Equals("id", StringComparison.OrdinalIgnoreCase));
            if (idCol != null)
                return idCol.ColumnName;

            // 4) Chịu, không đoán được
            return null;
        }



        private object? EvaluateDefaultExpr(string expr, CmsColumnMeta col)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return null;

            expr = expr.Trim();

            if (string.Equals(expr, "NOW", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now;

            if (string.Equals(expr, "UTC_NOW", StringComparison.OrdinalIgnoreCase))
                return DateTime.UtcNow;

            if (string.Equals(expr, "CURRENT_USER_ID", StringComparison.OrdinalIgnoreCase))
            {
                var uid = GetCurrentCmsUserId();
                return uid.HasValue ? (object)uid.Value : null;
            }

            if (expr.StartsWith("CONST:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = expr.Substring("CONST:".Length);
                return ConvertRawToType(raw, col);
            }

            // Có thể mở rộng thêm các kiểu khác sau này
            return null;
        }
    }
}
