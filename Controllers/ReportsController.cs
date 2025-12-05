using CmsTools.Models;
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
using System.Threading.Tasks;

namespace CmsTools.Controllers
{
    [Authorize(Policy = "CmsAdminOnly")]
    public sealed class ReportsController : Controller
    {
        private readonly string _hafConn;

        public ReportsController(IConfiguration cfg)
        {
            _hafConn = cfg.GetConnectionString("HAFoodDb")
                ?? throw new Exception("Missing connection string: HAFoodDb");
        }

        [HttpGet]
        public async Task<IActionResult> Revenue(
            DateTime? fromDate,
            DateTime? toDate,
            string? groupBy)
        {
            var from = fromDate ?? DateTime.Today.AddDays(-6); // 7 ngày gần nhất
            var to = toDate ?? DateTime.Today;
            var gb = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy;

            var vm = new RevenueReportViewModel
            {
                FromDate = from,
                ToDate = to,
                GroupBy = gb
            };

            using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync();

            // ----- Kỳ hiện tại -----
            var p = new DynamicParameters();
            p.Add("@from_date", from.Date);
            p.Add("@to_date", to.Date);
            p.Add("@group_by", gb);

            using (var multi = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue",
                       p,
                       commandType: CommandType.StoredProcedure))
            {
                vm.Overview = await multi.ReadFirstOrDefaultAsync<RevenueOverview>()
                               ?? new RevenueOverview();

                vm.Rows = (await multi.ReadAsync<RevenueByPeriodRow>()).ToList();

                vm.Providers = (await multi.ReadAsync<RevenueByProviderRow>()).ToList();
            }

            // ----- Kỳ trước (để so sánh) -----
            var daysSpan = (to.Date - from.Date).TotalDays + 1; // số ngày bao gồm cả from & to
            if (daysSpan < 1) daysSpan = 1;

            var prevTo = from.Date.AddDays(-1);
            var prevFrom = prevTo.AddDays(-daysSpan + 1);

            var pPrev = new DynamicParameters();
            pPrev.Add("@from_date", prevFrom);
            pPrev.Add("@to_date", prevTo);
            pPrev.Add("@group_by", gb);

            using (var multiPrev = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue",
                       pPrev,
                       commandType: CommandType.StoredProcedure))
            {
                vm.PreviousOverview = await multiPrev.ReadFirstOrDefaultAsync<RevenueOverview>()
                                      ?? new RevenueOverview();
                // bỏ qua Rows/Providers của kỳ trước
            }

            // Tính % thay đổi
            if (vm.PreviousOverview.TotalPayTotal > 0)
            {
                vm.RevenueChangePercent =
                    (vm.Overview.TotalPayTotal - vm.PreviousOverview.TotalPayTotal)
                    * 100m / vm.PreviousOverview.TotalPayTotal;
            }

            if (vm.PreviousOverview.TotalOrders > 0)
            {
                vm.OrdersChangePercent =
                    (vm.Overview.TotalOrders - vm.PreviousOverview.TotalOrders)
                    * 100m / vm.PreviousOverview.TotalOrders;
            }

            return View(vm);
        }

        /// <summary>
        /// Export báo cáo doanh thu.
        /// format = csv | excel (excel vẫn là CSV nhưng content-type khác).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> RevenueExport(
            DateTime? fromDate,
            DateTime? toDate,
            string? groupBy,
            string format = "csv")
        {
            var from = fromDate ?? DateTime.Today.AddDays(-6);
            var to = toDate ?? DateTime.Today;
            var gb = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy;

            using var conn = new SqlConnection(_hafConn);
            await conn.OpenAsync();

            var p = new DynamicParameters();
            p.Add("@from_date", from.Date);
            p.Add("@to_date", to.Date);
            p.Add("@group_by", gb);

            RevenueOverview overview;
            var rows = Enumerable.Empty<RevenueByPeriodRow>();

            using (var multi = await conn.QueryMultipleAsync(
                       "dbo.usp_report_revenue",
                       p,
                       commandType: CommandType.StoredProcedure))
            {
                overview = await multi.ReadFirstOrDefaultAsync<RevenueOverview>()
                           ?? new RevenueOverview();

                rows = await multi.ReadAsync<RevenueByPeriodRow>();

                // Đọc providers để "consume" hết result-set (phòng sau này SP đổi)
                var _ = await multi.ReadAsync<RevenueByProviderRow>();
            }

            var sb = new StringBuilder();

            // Header thông tin báo cáo
            sb.AppendLine("Bao cao doanh thu,HAFood");
            sb.AppendLine("Tu ngay," + from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            sb.AppendLine("Den ngay," + to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            sb.AppendLine("Nhom theo," + (gb == "month" ? "Thang" : "Ngay"));
            sb.AppendLine();
            sb.AppendLine("Tong so don," + overview.TotalOrders.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Tong doanh thu (pay_total)," +
                          overview.TotalPayTotal.ToString("0.##", CultureInfo.InvariantCulture));
            sb.AppendLine();

            // Header cột dữ liệu chi tiết
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

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = "csv";
            var fileName = $"revenue_{from:yyyyMMdd}_{to:yyyyMMdd}_{gb}.{ext}";

            var contentType = format == "excel"
                ? "application/vnd.ms-excel"
                : "text/csv; charset=utf-8";

            return File(bytes, contentType, fileName);
        }
    }
}
