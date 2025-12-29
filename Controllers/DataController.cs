using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace CmsTools.Controllers
{
    [Authorize]
    public class DataController : Controller
    {
        private readonly ICmsMetaService _meta;
        private readonly ICmsDbRouter _router;
        private readonly ICmsPermissionService _perms;
        private readonly ICmsAuditLogger _audit;
        private readonly IMemoryCache _cache;

        // cache lookup FK 10–30 phút (mặc định 20)
        private static readonly TimeSpan LookupCacheTtlDefault = TimeSpan.FromMinutes(20);

        public DataController(
            ICmsMetaService meta,
            ICmsDbRouter router,
            ICmsPermissionService perms,
            ICmsAuditLogger audit,
            IMemoryCache cache)
        {
            _meta = meta;
            _router = router;
            _perms = perms;
            _audit = audit;
            _cache = cache;
        }

        // =========================
        // LIST
        // =========================
        [HttpGet]
        public async Task<IActionResult> List(int tableId, int page = 1, int pageSize = 50)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 50 : pageSize;
            pageSize = Math.Min(pageSize, 200); // tránh kéo quá nặng

            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView) return Forbid();

            var allCols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!allCols.Any()) return BadRequest("No columns configured.");

            var listCols = allCols.Where(c => c.IsList).ToList();
            if (!listCols.Any()) listCols = allCols.ToList();

            var filterCols = allCols.Where(c => c.IsFilter).ToList();

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            // ===== build filters from querystring =====
            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var filterSqlParts = new List<string>();
            var filterParams = new Dictionary<string, object?>();

            foreach (var col in filterCols)
            {
                var raw = Request.Query[col.ColumnName].ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                filters[col.ColumnName] = raw;

                var paramName = "f_" + col.ColumnName;
                var dt = (col.DataType ?? "").ToLowerInvariant();

                if (IsTextType(dt))
                {
                    filterSqlParts.Add($"[{col.ColumnName}] LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw.Trim() + "%";
                }
                else if (IsIntType(dt))
                {
                    if (long.TryParse(NormalizeNumberRaw(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = num;
                    }
                }
                else if (IsDecimalType(dt))
                {
                    if (decimal.TryParse(NormalizeDecimalRaw(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = d;
                    }
                }
                else if (IsDateType(dt))
                {
                    if (TryParseDate(raw, out var d))
                    {
                        filterSqlParts.Add($"CAST([{col.ColumnName}] AS date) = @{paramName}");
                        filterParams[paramName] = d.Date;
                    }
                }
                else
                {
                    filterSqlParts.Add($"CAST([{col.ColumnName}] AS nvarchar(255)) LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw.Trim() + "%";
                }
            }

            // where = table.RowFilter + perm.RowFilter + user filters
            string? where = table.RowFilter;

            if (!string.IsNullOrWhiteSpace(perm.RowFilter))
            {
                where = string.IsNullOrWhiteSpace(where)
                    ? perm.RowFilter
                    : "(" + where + ") AND (" + perm.RowFilter + ")";
            }

            if (filterSqlParts.Any())
            {
                var userWhere = string.Join(" AND ", filterSqlParts);
                where = string.IsNullOrWhiteSpace(where)
                    ? userWhere
                    : "(" + where + ") AND (" + userWhere + ")";
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

            // ✅ lookup FK (cache)
            ViewBag.Lookups = await BuildLookupsAsync(connMeta, listCols, HttpContext.RequestAborted);

            return View(vm);
        }

        // =========================
        // EXPORT CSV
        // =========================
        [HttpGet]
        public async Task<IActionResult> ExportCsv(int tableId, int pageSize = 50000)
        {
            pageSize = pageSize < 1 ? 50000 : pageSize;
            pageSize = Math.Min(pageSize, 200000);

            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView) return Forbid();

            var allCols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!allCols.Any()) return BadRequest("No columns configured.");

            var listCols = allCols.Where(c => c.IsList).ToList();
            if (!listCols.Any()) listCols = allCols.ToList();

            var filterCols = allCols.Where(c => c.IsFilter).ToList();

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var filterSqlParts = new List<string>();
            var filterParams = new Dictionary<string, object?>();

            foreach (var col in filterCols)
            {
                var raw = Request.Query[col.ColumnName].ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var paramName = "f_" + col.ColumnName;
                var dt = (col.DataType ?? "").ToLowerInvariant();

                if (IsTextType(dt))
                {
                    filterSqlParts.Add($"[{col.ColumnName}] LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw.Trim() + "%";
                }
                else if (IsIntType(dt))
                {
                    if (long.TryParse(NormalizeNumberRaw(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = num;
                    }
                }
                else if (IsDecimalType(dt))
                {
                    if (decimal.TryParse(NormalizeDecimalRaw(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    {
                        filterSqlParts.Add($"[{col.ColumnName}] = @{paramName}");
                        filterParams[paramName] = d;
                    }
                }
                else if (IsDateType(dt))
                {
                    if (TryParseDate(raw, out var d))
                    {
                        filterSqlParts.Add($"CAST([{col.ColumnName}] AS date) = @{paramName}");
                        filterParams[paramName] = d.Date;
                    }
                }
                else
                {
                    filterSqlParts.Add($"CAST([{col.ColumnName}] AS nvarchar(255)) LIKE @{paramName}");
                    filterParams[paramName] = "%" + raw.Trim() + "%";
                }
            }

            string? where = table.RowFilter;

            if (!string.IsNullOrWhiteSpace(perm.RowFilter))
            {
                where = string.IsNullOrWhiteSpace(where)
                    ? perm.RowFilter
                    : "(" + where + ") AND (" + perm.RowFilter + ")";
            }

            if (filterSqlParts.Any())
            {
                var userWhere = string.Join(" AND ", filterSqlParts);
                where = string.IsNullOrWhiteSpace(where)
                    ? userWhere
                    : "(" + where + ") AND (" + userWhere + ")";
            }

            var result = await _router.QueryAsync(
                connMeta,
                table,
                listCols,
                where,
                filterParams,
                page: 1,
                pageSize: pageSize);

            var sb = new StringBuilder();

            for (int i = 0; i < listCols.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeCsv(listCols[i].DisplayName ?? listCols[i].ColumnName));
            }
            sb.AppendLine();

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

            // ✅ BOM để Excel mở tiếng Việt OK
            var utf8Bom = Encoding.UTF8.GetPreamble();
            var payload = Encoding.UTF8.GetBytes(sb.ToString());
            var bytes = utf8Bom.Concat(payload).ToArray();

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        private static string EscapeCsv(string s)
        {
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            {
                s = s.Replace("\"", "\"\"");
                return $"\"{s}\"";
            }
            return s;
        }

        // =========================
        // EDIT (GET)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate && !perm.CanView) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var row = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);
            if (row == null) return NotFound("Row not found.");

            ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            return View(vm);
        }

        // =========================
        // EDIT (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int tableId, long id, IFormCollection form)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var oldRow = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);

            IReadOnlyDictionary<string, object?>? oldSnapshot = null;
            if (oldRow != null)
            {
                oldSnapshot = new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase));
            }

            var editableCols = cols
                .Where(c => c.IsEditable && !c.IsPrimary && !c.ColumnName.Equals(pkCol, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in editableCols)
            {
                var raw = form[col.ColumnName].ToString();
                raw = NormalizeFormRaw(raw, col);

                if (IsBitCol(col))
                {
                    values[col.ColumnName] = ConvertRawToType(raw, col);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (!col.IsNullable && string.IsNullOrWhiteSpace(col.DefaultExpr))
                    {
                        ModelState.AddModelError(col.ColumnName, "Trường này là bắt buộc.");
                    }
                    else
                    {
                        if (col.IsNullable) values[col.ColumnName] = null;
                    }
                    continue;
                }

                values[col.ColumnName] = ConvertRawToType(raw, col);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                var vmInvalid = new DataEditViewModel
                {
                    Table = table,
                    Columns = cols,
                    Values = oldRow ?? new Dictionary<string, object?>(),
                    PrimaryKeyColumn = pkCol,
                    PrimaryKeyValue = id
                };

                if (IsAjaxRequest())
                    return PartialView("_EditForm", vmInvalid);

                return View(vmInvalid);
            }

            IReadOnlyDictionary<string, object?>? newSnapshot = null;
            if (oldRow != null)
            {
                var newRowDict = new Dictionary<string, object?>(oldRow, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in values) newRowDict[kv.Key] = kv.Value;
                newSnapshot = new ReadOnlyDictionary<string, object?>(newRowDict);
            }

            var editableColsToUpdate = editableCols
                .Where(c => values.ContainsKey(c.ColumnName))
                .ToList();

            var affected = await _router.UpdateRowAsync(
                connMeta,
                table,
                editableColsToUpdate,
                pkCol,
                id,
                values);

            if (affected <= 0)
            {
                ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                var vmFail = new DataEditViewModel
                {
                    Table = table,
                    Columns = cols,
                    Values = oldRow ?? new Dictionary<string, object?>(),
                    PrimaryKeyColumn = pkCol,
                    PrimaryKeyValue = id
                };

                ModelState.AddModelError(string.Empty, "Update failed.");

                if (IsAjaxRequest())
                    return PartialView("_EditForm", vmFail);

                return View(vmFail);
            }

            await _audit.LogAsync(
                userId,
                "UPDATE",
                table,
                pkCol,
                id,
                oldSnapshot,
                newSnapshot);

            if (IsAjaxRequest())
                return Json(new { ok = true });

            return RedirectToAction("List", new { tableId });
        }

        // =========================
        // CREATE (GET)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create(int tableId)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanCreate) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var vm = new DataCreateViewModel
            {
                Table = table,
                Columns = cols
            };

            ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);
            return View(vm);
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int tableId, IFormCollection form)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanCreate) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var editableFromForm = cols
                .Where(c => c.IsEditable && !c.IsPrimary)
                .ToList();

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var finalCols = new List<CmsColumnMeta>();

            foreach (var col in editableFromForm)
            {
                var raw = form[col.ColumnName].ToString();
                raw = NormalizeFormRaw(raw, col);

                if (IsBitCol(col))
                {
                    values[col.ColumnName] = ConvertRawToType(raw, col);
                    finalCols.Add(col);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (!col.IsNullable && string.IsNullOrWhiteSpace(col.DefaultExpr))
                    {
                        ModelState.AddModelError(col.ColumnName, "Trường này là bắt buộc.");
                    }
                    continue;
                }

                values[col.ColumnName] = ConvertRawToType(raw, col);
                finalCols.Add(col);
            }

            // apply default_expr cho cột chưa có
            foreach (var col in cols)
            {
                if (col.IsPrimary) continue;
                if (values.ContainsKey(col.ColumnName)) continue;
                if (string.IsNullOrWhiteSpace(col.DefaultExpr)) continue;

                var defVal = EvaluateDefaultExpr(col.DefaultExpr!, col);
                if (defVal != null || col.IsNullable)
                {
                    values[col.ColumnName] = defVal;
                    if (!finalCols.Any(x => x.ColumnName.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase)))
                        finalCols.Add(col);
                }
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                var vmInvalid = new DataCreateViewModel
                {
                    Table = table,
                    Columns = cols
                };

                if (IsAjaxRequest())
                    return PartialView("_CreateForm", vmInvalid);

                return View(vmInvalid);
            }

            try
            {
                var newId = await _router.InsertRowAsync(connMeta, table, finalCols, values);
                if (newId <= 0)
                {
                    ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                    var vmFail = new DataCreateViewModel
                    {
                        Table = table,
                        Columns = cols
                    };

                    ModelState.AddModelError(string.Empty, "Insert failed.");

                    if (IsAjaxRequest())
                        return PartialView("_CreateForm", vmFail);

                    return View(vmFail);
                }

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
                    null,
                    newSnapshot);

                if (IsAjaxRequest())
                    return Json(new { ok = true });

                return RedirectToAction("List", new { tableId });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);

                ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                var vm = new DataCreateViewModel
                {
                    Table = table,
                    Columns = cols
                };

                if (IsAjaxRequest())
                    return PartialView("_CreateForm", vm);

                return View(vm);
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);

                ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

                var vm = new DataCreateViewModel
                {
                    Table = table,
                    Columns = cols
                };

                if (IsAjaxRequest())
                    return PartialView("_CreateForm", vm);

                return View(vm);
            }
        }

        // =========================
        // SET STATUS
        // =========================
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
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate && !perm.CanDelete) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var statusCol = FindStatusColumn(cols);
            if (statusCol == null) return BadRequest("Status column not found.");

            var editableCols = new List<CmsColumnMeta> { statusCol };
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [statusCol.ColumnName] = status
            };

            var oldRow = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);

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

            var affected = await _router.UpdateRowAsync(connMeta, table, editableCols, pkCol, id, values);
            if (affected <= 0) return BadRequest("Update status failed.");

            await _audit.LogAsync(userId, "SET_STATUS", table, pkCol, id, oldSnapshot, newSnapshot);

            return RedirectToAction("List", new { tableId });
        }

        // =========================
        // DETAILS
        // =========================
        [HttpGet]
        public async Task<IActionResult> Details(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var row = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);
            if (row == null) return NotFound("Row not found.");

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> DetailsModal(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanView) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var row = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);
            if (row == null) return NotFound("Row not found.");

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            return PartialView("_DetailsModal", vm);
        }

        // =========================
        // CREATE FORM (AJAX)
        // =========================
        [HttpGet]
        public async Task<IActionResult> CreateForm(int tableId)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanCreate) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var vm = new DataCreateViewModel
            {
                Table = table,
                Columns = cols
            };

            ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

            if (IsAjaxRequest())
                return PartialView("_CreateForm", vm);

            return View("Create", vm);
        }

        // =========================
        // EDIT FORM (AJAX)
        // =========================
        [HttpGet]
        public async Task<IActionResult> EditForm(int tableId, long id)
        {
            var table = await _meta.GetTableAsync(tableId);
            if (table == null) return NotFound("Table metadata not found or disabled.");

            var userId = GetCurrentCmsUserId();
            if (userId == null) return Unauthorized();

            var perm = await _perms.GetTablePermissionAsync(userId.Value, tableId);
            if (!perm.CanUpdate && !perm.CanView) return Forbid();

            var cols = await _meta.GetColumnsAsync(tableId, forList: false);
            if (!cols.Any()) return BadRequest("No columns configured.");

            var connMeta = await _meta.GetConnectionAsync(table.ConnectionId);
            if (connMeta == null || !connMeta.IsActive) return BadRequest("Connection not available.");

            var pkCol = ResolvePrimaryKey(table, cols);
            if (string.IsNullOrWhiteSpace(pkCol)) return BadRequest("Primary key not configured.");

            var row = await _router.GetRowAsync(connMeta, table, cols, pkCol, id);
            if (row == null) return NotFound("Row not found.");

            var vm = new DataEditViewModel
            {
                Table = table,
                Columns = cols,
                Values = row,
                PrimaryKeyColumn = pkCol,
                PrimaryKeyValue = id
            };

            ViewBag.Lookups = await BuildLookupsAsync(connMeta, cols, HttpContext.RequestAborted);

            if (IsAjaxRequest())
                return PartialView("_EditForm", vm);

            return View("Edit", vm);
        }

        // =========================
        // HELPERS
        // =========================
        private int? GetCurrentCmsUserId()
        {
            var claim = HttpContext.User?.FindFirst("cms_user_id");
            if (claim != null && int.TryParse(claim.Value, out var id))
                return id;

            return null;
        }

        private static bool IsBitCol(CmsColumnMeta col)
        {
            var t = (col.DataType ?? "").ToLowerInvariant();
            return t.StartsWith("bit");
        }

        private static string NormalizeFormRaw(string raw, CmsColumnMeta col)
        {
            if (IsBitCol(col) && !string.IsNullOrWhiteSpace(raw) && raw.Contains(","))
            {
                var last = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                return last?.Trim() ?? raw;
            }
            return raw;
        }

        private static object? ConvertRawToType(string raw, CmsColumnMeta col)
        {
            var t = (col.DataType ?? string.Empty).ToLowerInvariant();

            if (t.StartsWith("bit"))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                raw = raw.Trim();
                if (raw.Contains(","))
                    raw = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? raw;

                if (raw.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    raw == "1")
                    return true;

                return false;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (col.IsNullable) return null;
                return null;
            }

            try
            {
                if (t.StartsWith("bigint"))
                    return long.Parse(NormalizeNumberRaw(raw), CultureInfo.InvariantCulture);

                if (t.StartsWith("int") || t.StartsWith("smallint") || t.StartsWith("tinyint"))
                    return int.Parse(NormalizeNumberRaw(raw), CultureInfo.InvariantCulture);

                if (IsDecimalType(t))
                    return decimal.Parse(NormalizeDecimalRaw(raw), CultureInfo.InvariantCulture);

                if (t.StartsWith("float") || t.StartsWith("real"))
                    return double.Parse(NormalizeDecimalRaw(raw), CultureInfo.InvariantCulture);

                if (IsDateType(t))
                {
                    if (TryParseDate(raw, out var dtExact))
                        return dtExact;

                    return DateTime.Parse(raw, CultureInfo.CurrentCulture);
                }

                return raw;
            }
            catch
            {
                return raw;
            }
        }

        private static string? ResolvePrimaryKey(CmsTableMeta table, IReadOnlyList<CmsColumnMeta> cols)
        {
            if (!string.IsNullOrWhiteSpace(table.PrimaryKey))
                return table.PrimaryKey;

            var pk = cols.FirstOrDefault(c => c.IsPrimary)?.ColumnName;
            if (!string.IsNullOrWhiteSpace(pk))
                return pk;

            var idCol = cols.FirstOrDefault(c =>
                c.ColumnName.Equals("id", StringComparison.OrdinalIgnoreCase));
            if (idCol != null)
                return idCol.ColumnName;

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

            return null;
        }

        private bool IsAjaxRequest()
        {
            var h = Request.Headers["X-Requested-With"].ToString();
            return h.Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        // ========= FK LOOKUPS (cached) =========
        private static bool TryParseFkFormat(string? format, out string table, out string valueCol, out string textCol)
        {
            table = valueCol = textCol = "";
            if (string.IsNullOrWhiteSpace(format)) return false;

            format = format.Trim();
            if (!format.StartsWith("fk:", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = format.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;

            table = parts[1].Trim();
            valueCol = parts[2].Trim();
            textCol = parts[3].Trim();

            return !(string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(valueCol) || string.IsNullOrWhiteSpace(textCol));
        }

        private static readonly Regex RxSafeIdent = new(@"^[A-Za-z0-9_\.\[\]]+$", RegexOptions.Compiled);
        private static bool IsSafeIdent(string s) => !string.IsNullOrWhiteSpace(s) && RxSafeIdent.IsMatch(s);

        private async Task<Dictionary<string, List<(string Value, string Text)>>> BuildLookupsAsync(
            CmsConnectionMeta connMeta,
            IReadOnlyList<CmsColumnMeta> cols,
            CancellationToken ct)
        {
            var dict = new Dictionary<string, List<(string Value, string Text)>>(StringComparer.OrdinalIgnoreCase);

            var fkCols = cols.Where(c =>
                    !string.IsNullOrWhiteSpace(c.ColumnName)
                    && !string.IsNullOrWhiteSpace(c.Format)
                    && c.Format.StartsWith("fk:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!fkCols.Any()) return dict;

            await using var db = new SqlConnection(connMeta.ConnString);
            await db.OpenAsync(ct);

            foreach (var col in fkCols)
            {
                if (!TryParseFkFormat(col.Format, out var fkTable, out var valueCol, out var textCol))
                    continue;

                if (!IsSafeIdent(fkTable) || !IsSafeIdent(valueCol) || !IsSafeIdent(textCol))
                    continue;

                var cacheKey = $"cms:lookup:{connMeta.Id}:{fkTable}:{valueCol}:{textCol}";
                if (_cache.TryGetValue(cacheKey, out List<(string Value, string Text)> cached))
                {
                    dict[col.ColumnName!] = cached;
                    continue;
                }

                var fullTable = fkTable.Contains(".") ? fkTable : $"dbo.{fkTable}";
                var baseName = fkTable.Contains(".") ? fkTable.Split('.').Last() : fkTable;

                var isCategoryTree = baseName.Equals("tbl_product_category", StringComparison.OrdinalIgnoreCase);

                List<(string Value, string Text)> items;

                if (isCategoryTree)
                {
                    var sqlTree = $@"
;WITH cte AS (
    SELECT
        CAST([{valueCol}] AS bigint) AS id,
        CAST([parent_id] AS bigint)  AS parent_id,
        CAST([{textCol}] AS nvarchar(300)) AS name,
        0 AS lvl,
        CAST(
            RIGHT('0000000000' + CAST(ISNULL([sort_order], 0) AS varchar(10)), 10)
            + '|' + RIGHT('0000000000' + CAST([{valueCol}] AS varchar(10)), 10)
        AS nvarchar(4000)) AS path
    FROM {fullTable}
    WHERE [parent_id] IS NULL

    UNION ALL

    SELECT
        CAST(c.[{valueCol}] AS bigint) AS id,
        CAST(c.[parent_id] AS bigint)  AS parent_id,
        CAST(c.[{textCol}] AS nvarchar(300)) AS name,
        p.lvl + 1 AS lvl,
        CAST(
            p.path + '/' +
            RIGHT('0000000000' + CAST(ISNULL(c.[sort_order], 0) AS varchar(10)), 10)
            + '|' + RIGHT('0000000000' + CAST(c.[{valueCol}] AS varchar(10)), 10)
        AS nvarchar(4000)) AS path
    FROM {fullTable} c
    JOIN cte p ON c.[parent_id] = p.id
)
SELECT TOP 2000
    CAST(id AS nvarchar(50)) AS [Value],
    (REPLICATE(N'-- ', lvl) + name + N' (#' + CAST(id AS nvarchar(50)) + N')') AS [Text]
FROM cte
ORDER BY path;";

                    try
                    {
                        items = (await db.QueryAsync<(string Value, string Text)>(
                            new CommandDefinition(sqlTree, cancellationToken: ct))).ToList();
                    }
                    catch
                    {
                        items = new List<(string, string)>();
                    }

                    if (items.Count > 0)
                    {
                        _cache.Set(cacheKey, items, LookupCacheTtlDefault);
                        dict[col.ColumnName!] = items;
                        continue;
                    }
                }

                var sqlWithSort = $@"
SELECT TOP 2000
    CAST([{valueCol}] AS nvarchar(50)) AS [Value],
    CAST([{textCol}] AS nvarchar(300)) + N' (#' + CAST([{valueCol}] AS nvarchar(50)) + N')' AS [Text]
FROM {fullTable}
ORDER BY [sort_order], [{textCol}];";

                try
                {
                    items = (await db.QueryAsync<(string Value, string Text)>(
                        new CommandDefinition(sqlWithSort, cancellationToken: ct))).ToList();

                    _cache.Set(cacheKey, items, LookupCacheTtlDefault);
                    dict[col.ColumnName!] = items;
                    continue;
                }
                catch { }

                var sqlFallback = $@"
SELECT TOP 2000
    CAST([{valueCol}] AS nvarchar(50)) AS [Value],
    CAST([{textCol}] AS nvarchar(300)) + N' (#' + CAST([{valueCol}] AS nvarchar(50)) + N')' AS [Text]
FROM {fullTable}
ORDER BY [{textCol}];";

                try
                {
                    items = (await db.QueryAsync<(string Value, string Text)>(
                        new CommandDefinition(sqlFallback, cancellationToken: ct))).ToList();

                    _cache.Set(cacheKey, items, LookupCacheTtlDefault);
                    dict[col.ColumnName!] = items;
                }
                catch { }
            }

            return dict;
        }

        // ========= type helpers =========
        private static bool IsTextType(string dt) => dt.Contains("char") || dt.Contains("text") || dt.Contains("ntext");

        private static bool IsIntType(string dt)
            => dt.StartsWith("tinyint") || dt.StartsWith("smallint") || dt.StartsWith("int") || dt.StartsWith("bigint");

        private static bool IsDecimalType(string dt)
            => dt.StartsWith("decimal") || dt.StartsWith("numeric") || dt.StartsWith("money") || dt.StartsWith("smallmoney");

        private static bool IsDateType(string dt)
            => dt.StartsWith("date") || dt.StartsWith("datetime") || dt.StartsWith("smalldatetime") || dt.StartsWith("datetime2");

        private static string NormalizeNumberRaw(string raw)
        {
            raw = (raw ?? "").Trim();
            raw = raw.Replace(" ", "");
            raw = raw.Replace(",", "");
            raw = raw.Replace(".", "");
            return raw;
        }

        private static string NormalizeDecimalRaw(string raw)
        {
            raw = (raw ?? "").Trim();
            raw = raw.Replace(" ", "");

            // có cả '.' và ',' => assume '.' nghìn, ',' thập phân (vi-VN)
            if (raw.Contains('.') && raw.Contains(','))
            {
                raw = raw.Replace(".", "");
                raw = raw.Replace(",", ".");
                return raw;
            }

            // chỉ có ',' => coi là thập phân
            if (raw.Contains(',') && !raw.Contains('.'))
            {
                raw = raw.Replace(",", ".");
                return raw;
            }

            return raw;
        }

        private static bool TryParseDate(string raw, out DateTime dt)
        {
            raw = (raw ?? "").Trim();
            return DateTime.TryParseExact(
                       raw,
                       new[] { "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                       CultureInfo.GetCultureInfo("vi-VN"),
                       DateTimeStyles.AllowWhiteSpaces,
                       out dt)
                   || DateTime.TryParse(raw, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.AllowWhiteSpaces, out dt);
        }
    }
}
