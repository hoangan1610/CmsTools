using CmsTools.Models;
using CmsTools.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class ReportsController : Controller
    {
        private readonly string _hafConn;
        private readonly IRevenueAiInsightService _revAi;

        public ReportsController(IConfiguration cfg, IRevenueAiInsightService revAi)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");
            _revAi = revAi;
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

        [HttpGet]
        public async Task<IActionResult> Revenue(DateTime? fromDate, DateTime? toDate, string? groupBy)
        {
            var vnToday = GetVnNow().Date;

            var from = (fromDate ?? vnToday.AddDays(-6));
            var to = (toDate ?? vnToday);

            NormalizeDateRange(ref from, ref to, maxDays: 366);

            var gb = NormalizeGroupBy(groupBy);

            var vm = new RevenueReportViewModel
            {
                FromDate = from,
                ToDate = to,
                GroupBy = gb
            };

            await using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync();

            // ----- Kỳ hiện tại -----
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

            // ----- Kỳ trước -----
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

            // % thay đổi
            vm.RevenueChangePercent = (vm.PreviousOverview.TotalPayTotal > 0)
                ? (vm.Overview.TotalPayTotal - vm.PreviousOverview.TotalPayTotal) * 100m / vm.PreviousOverview.TotalPayTotal
                : (vm.Overview.TotalPayTotal > 0 ? 100m : 0m);

            vm.OrdersChangePercent = (vm.PreviousOverview.TotalOrders > 0)
                ? (vm.Overview.TotalOrders - vm.PreviousOverview.TotalOrders) * 100m / vm.PreviousOverview.TotalOrders
                : (vm.Overview.TotalOrders > 0 ? 100m : 0m);

            // ✅ AI insight: dùng linked token + giới hạn thời gian
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            cts.CancelAfter(TimeSpan.FromMinutes(3));
            vm.Ai = await _revAi.BuildAsync(vm, cts.Token);

            return View(vm);
        }
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

            // mặc định lấy dữ liệu bán 30 ngày gần nhất để dự báo tháng tới
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
                _ = await multi.ReadAsync<RevenueByProviderRow>(); // consume
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
