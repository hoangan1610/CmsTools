using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class ReportsController : Controller
    {
        private readonly string _hafConn;
        private readonly IRevenueAiInsightService _revAi;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _cfg;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            IConfiguration cfg,
            IRevenueAiInsightService revAi,
            IMemoryCache cache,
            ILogger<ReportsController> logger)
        {
            _cfg = cfg;
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");
            _revAi = revAi;
            _cache = cache;
            _logger = logger;
        }

        private static DateTime GetVnNow()
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
        }

        private static string NormalizeGroupBy(string? groupBy)
        {
            var gb = (groupBy ?? "").Trim().ToLowerInvariant();
            return (gb == "month") ? "month" : "day";
        }

        private static void NormalizeDateRange(ref DateTime from, ref DateTime to, int maxDays = 366)
        {
            from = from.Date;
            to = to.Date;

            if (from > to)
            {
                var tmp = from;
                from = to;
                to = tmp;
            }

            var days = (to - from).TotalDays + 1;
            if (days < 1) days = 1;

            if (days > maxDays)
            {
                from = to.AddDays(-(maxDays - 1));
            }
        }

        private static string GetClaimOrEmpty(ClaimsPrincipal user, params string[] claimTypes)
        {
            foreach (var t in claimTypes)
            {
                var v = user.FindFirstValue(t);
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return "";
        }

        private string GetTenantKey()
        {
            var tenant =
                GetClaimOrEmpty(User, "tenant_id", "TenantId", "tenant", "shop_id", "ShopId", "ClientId");

            return string.IsNullOrWhiteSpace(tenant) ? "tenant:default" : ("tenant:" + tenant);
        }

        private string GetUserKey()
        {
            var uid =
                GetClaimOrEmpty(User, ClaimTypes.NameIdentifier, "sub", "user_id", "UserId");

            if (string.IsNullOrWhiteSpace(uid))
                uid = User?.Identity?.Name ?? "anonymous";

            return "user:" + uid.Trim();
        }

        private static string BuildFiltersKey(Microsoft.AspNetCore.Http.IQueryCollection q)
        {
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // "format", "page"
            };

            var parts = q
                .Where(kv => !ignored.Contains(kv.Key))
                .Select(kv =>
                {
                    var key = kv.Key.Trim();
                    var val = string.Join("|", kv.Value.ToArray()).Trim();
                    return (k: key, v: val);
                })
                .OrderBy(x => x.k, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.v, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.k}={x.v}");

            return string.Join("&", parts);
        }

        private string BuildRevenueAiCacheKey(DateTime from, DateTime to, string groupBy)
        {
            var tenantKey = GetTenantKey();
            var userKey = GetUserKey();
            var filtersKey = BuildFiltersKey(Request.Query);

            return $"rev-ai|{tenantKey}|{userKey}|from:{from:yyyyMMdd}|to:{to:yyyyMMdd}|gb:{groupBy}|q:{filtersKey}";
        }

        private MemoryCacheEntryOptions BuildAiCacheOptions()
        {
            var minutes = _cfg.GetValue<int?>("Reports:RevenueAi:CacheMinutes") ?? 20;
            if (minutes < 10) minutes = 10;
            if (minutes > 30) minutes = 30;

            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes)
            };
        }

        private async Task<RevenueReportViewModel> BuildRevenueVmAsync(DateTime from, DateTime to, string gb, CancellationToken ct)
        {
            var vm = new RevenueReportViewModel
            {
                FromDate = from,
                ToDate = to,
                GroupBy = gb
            };

            await using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync(ct);

            // ======================================
            // A) Revenue recognized (delivered)
            // ======================================
            var p = new DynamicParameters();
            p.Add("@from_date", from);
            p.Add("@to_date", to);
            p.Add("@group_by", gb);

            using (var multi = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue", p, commandType: CommandType.StoredProcedure))
            {
                vm.Overview = await multi.ReadFirstOrDefaultAsync<RevenueOverview>() ?? new RevenueOverview();
                vm.Rows = (await multi.ReadAsync<RevenueByPeriodRow>()).ToList();
                vm.Providers = (await multi.ReadAsync<RevenueByProviderRow>()).ToList();
            }

            // ======================================
            // B) Previous period (for delta)
            // ======================================
            var daysSpan = (int)((to - from).TotalDays + 1);
            if (daysSpan < 1) daysSpan = 1;

            var prevTo = from.AddDays(-1);
            var prevFrom = prevTo.AddDays(-(daysSpan - 1));

            var pPrev = new DynamicParameters();
            pPrev.Add("@from_date", prevFrom);
            pPrev.Add("@to_date", prevTo);
            pPrev.Add("@group_by", gb);

            using (var multiPrev = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue", pPrev, commandType: CommandType.StoredProcedure))
            {
                vm.PreviousOverview = await multiPrev.ReadFirstOrDefaultAsync<RevenueOverview>() ?? new RevenueOverview();
            }

            vm.RevenueChangePercent = (vm.PreviousOverview.TotalPayTotal > 0)
                ? (vm.Overview.TotalPayTotal - vm.PreviousOverview.TotalPayTotal) * 100m / vm.PreviousOverview.TotalPayTotal
                : (vm.Overview.TotalPayTotal > 0 ? 100m : 0m);

            vm.OrdersChangePercent = (vm.PreviousOverview.TotalOrders > 0)
                ? (vm.Overview.TotalOrders - vm.PreviousOverview.TotalOrders) * 100m / vm.PreviousOverview.TotalOrders
                : (vm.Overview.TotalOrders > 0 ? 100m : 0m);

            // ======================================
            // C) Cash-in (paid_at for online, delivered/received for COD)
            // Proc returns 3 result sets:
            //   (1) overview, (2) rows by period, (3) providers breakdown
            // ======================================
            using (var multiCash = await conn.QueryMultipleAsync(
                "dbo.usp_report_cashflow", p, commandType: CommandType.StoredProcedure))
            {
                vm.CashflowOverview = await multiCash.ReadFirstOrDefaultAsync<CashflowOverview>() ?? new CashflowOverview();
                vm.CashflowRows = (await multiCash.ReadAsync<CashflowByPeriodRow>()).ToList();
                vm.CashflowProviders = (await multiCash.ReadAsync<CashflowByProviderRow>()).ToList();
            }

            // ======================================
            // D) Pipeline snapshot (open orders)
            // as_of_excl = end of ToDate (00:00 of next day)
            // Proc returns 2 result sets:
            //   (1) snapshot, (2) breakdown by status
            // ======================================
            var asOfExcl = to.Date.AddDays(1);
            var pPipe = new DynamicParameters();
            pPipe.Add("@as_of_excl", asOfExcl);

            using (var multiPipe = await conn.QueryMultipleAsync(
                "dbo.usp_report_pipeline_snapshot", pPipe, commandType: CommandType.StoredProcedure))
            {
                vm.Pipeline = await multiPipe.ReadFirstOrDefaultAsync<PipelineSnapshot>() ?? new PipelineSnapshot();
                vm.PipelineByStatus = (await multiPipe.ReadAsync<PipelineByStatusRow>()).ToList();
            }

            // Không chạy AI ở đây
            vm.Ai = null;

            return vm;
        }

        // ===========================
        // 1) PAGE: Revenue (render nhanh, không gọi AI)
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Revenue(DateTime? fromDate, DateTime? toDate, string? groupBy)
        {
            var vnToday = GetVnNow().Date;

            var from = (fromDate ?? vnToday.AddDays(-6));
            var to = (toDate ?? vnToday);

            NormalizeDateRange(ref from, ref to, maxDays: 366);
            var gb = NormalizeGroupBy(groupBy);

            var vm = await BuildRevenueVmAsync(from, to, gb, HttpContext.RequestAborted);
            return View(vm);
        }

        // ===========================
        // 2) API: RevenueAi
        // ===========================
        [HttpGet]
        public async Task<IActionResult> RevenueAi(DateTime fromDate, DateTime toDate, string? groupBy, bool force = false)
        {
            var from = fromDate;
            var to = toDate;

            NormalizeDateRange(ref from, ref to, maxDays: 366);
            var gb = NormalizeGroupBy(groupBy);

            var cacheKey = BuildRevenueAiCacheKey(from, to, gb);

            if (!force && _cache.TryGetValue(cacheKey, out RevenueAiInsightResult cached) && cached != null)
            {
                return Ok(new { ok = true, cached = true, data = cached });
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            var timeoutSec = _cfg.GetValue<int?>("Reports:RevenueAi:TimeoutSeconds") ?? 180;
            if (timeoutSec < 30) timeoutSec = 30;
            if (timeoutSec > 300) timeoutSec = 300;
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            try
            {
                var vm = await BuildRevenueVmAsync(from, to, gb, cts.Token);

                if (vm.Rows == null || vm.Rows.Count == 0)
                {
                    var empty = new RevenueAiInsightResult
                    {
                        Summary = "Không có dữ liệu trong khoảng lọc.",
                        Highlights = new(),
                        Alerts = new()
                    };

                    _cache.Set(cacheKey, empty, BuildAiCacheOptions());
                    return Ok(new { ok = true, cached = false, data = empty });
                }

                var ai = await _revAi.BuildAsync(vm, cts.Token);

                if (ai == null)
                {
                    return Ok(new { ok = false, cached = false, message = "AI timeout hoặc bị huỷ.", data = (RevenueAiInsightResult?)null });
                }

                _cache.Set(cacheKey, ai, BuildAiCacheOptions());
                return Ok(new { ok = true, cached = false, data = ai });
            }
            catch (OperationCanceledException)
            {
                return Ok(new { ok = false, cached = false, message = "AI timeout hoặc người dùng huỷ request.", data = (RevenueAiInsightResult?)null });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RevenueAi failed");
                return Ok(new { ok = false, cached = false, message = "AI gặp lỗi.", data = (RevenueAiInsightResult?)null });
            }
        }

        // ===========================
        // giữ nguyên các action khác của bạn
        // ===========================

        [HttpGet]
        public async Task<IActionResult> ReorderSuggest(
            DateTime? fromDate,
            DateTime? toDate,
            int horizonDays = 30,
            decimal safetyFactor = 1.20m,
            int topN = 200,
            bool onlyActive = true,
            bool includeZeroSales = false)
        {
            var vnToday = GetVnNow().Date;

            var from = (fromDate ?? vnToday.AddDays(-29));
            var to = (toDate ?? vnToday);

            NormalizeDateRange(ref from, ref to, maxDays: 366);

            var vm = new ReorderSuggestViewModel
            {
                FromDate = from,
                ToDate = to,
                HorizonDays = (horizonDays < 1 ? 30 : horizonDays),
                SafetyFactor = (safetyFactor < 1 ? 1 : safetyFactor),
                TopN = (topN < 1 ? 200 : topN),
                OnlyActive = onlyActive,
                IncludeZeroSales = includeZeroSales
            };

            await using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync(HttpContext.RequestAborted);

            var p = new DynamicParameters();
            p.Add("@from_date", vm.FromDate.Date);
            p.Add("@to_date", vm.ToDate.Date);
            p.Add("@horizon_days", vm.HorizonDays);
            p.Add("@safety_factor", vm.SafetyFactor);
            p.Add("@top_n", vm.TopN);
            p.Add("@only_active", vm.OnlyActive);
            p.Add("@include_zero_sales", vm.IncludeZeroSales);

            var rows = await conn.QueryAsync<ReorderSuggestRow>(
                "dbo.usp_report_reorder_suggest",
                p,
                commandType: CommandType.StoredProcedure
            );

            vm.Rows = rows.ToList();
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> RevenueExport(
            DateTime? fromDate,
            DateTime? toDate,
            string? groupBy,
            string format = "csv")
        {
            var vnToday = GetVnNow().Date;

            var from = (fromDate ?? vnToday.AddDays(-6));
            var to = (toDate ?? vnToday);

            NormalizeDateRange(ref from, ref to, maxDays: 366);
            var gb = NormalizeGroupBy(groupBy);

            await using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@from_date", from);
            p.Add("@to_date", to);
            p.Add("@group_by", gb);

            RevenueOverview overview;
            var rows = Enumerable.Empty<RevenueByPeriodRow>();

            using (var multi = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue", p, commandType: CommandType.StoredProcedure))
            {
                overview = await multi.ReadFirstOrDefaultAsync<RevenueOverview>() ?? new RevenueOverview();
                rows = await multi.ReadAsync<RevenueByPeriodRow>();
                _ = await multi.ReadAsync<RevenueByProviderRow>();
            }

            var sb = new StringBuilder();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("sep=,");

            sb.AppendLine("Bao cao doanh thu,HAFood");
            sb.AppendLine("Tu ngay," + from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            sb.AppendLine("Den ngay," + to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            sb.AppendLine("Nhom theo," + (gb == "month" ? "Thang" : "Ngay"));
            sb.AppendLine();
            sb.AppendLine("Tong so don," + overview.TotalOrders.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Tong doanh thu (pay_total)," + overview.TotalPayTotal.ToString("0.##", CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("Ngay/Thang,So don,SubTotal,Giam gia,Phi ship,VAT,PayTotal");

            var dateFormat = gb == "month" ? "MM/yyyy" : "dd/MM/yyyy";

            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    r.PeriodDate.ToString(dateFormat, CultureInfo.InvariantCulture),
                    r.OrderCount.ToString(CultureInfo.InvariantCulture),
                    r.SubTotal.ToString("0.##", CultureInfo.InvariantCulture),
                    r.DiscountTotal.ToString("0.##", CultureInfo.InvariantCulture),
                    r.ShippingTotal.ToString("0.##", CultureInfo.InvariantCulture),
                    r.VatTotal.ToString("0.##", CultureInfo.InvariantCulture),
                    r.PayTotal.ToString("0.##", CultureInfo.InvariantCulture)
                }));
            }

            var utf8Bom = Encoding.UTF8.GetPreamble();
            var payload = Encoding.UTF8.GetBytes(sb.ToString());
            var bytes = utf8Bom.Concat(payload).ToArray();

            var fileName = $"revenue_{from:yyyyMMdd}_{to:yyyyMMdd}_{gb}.csv";
            var contentType = string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-excel"
                : "text/csv; charset=utf-8";

            return File(bytes, contentType, fileName);
        }
    }
}
