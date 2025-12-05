using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class RevenueOverview
    {
        public int TotalOrders { get; set; }

        public decimal TotalSubTotal { get; set; }         // tổng sub_total
        public decimal TotalDiscountTotal { get; set; }    // tổng discount_total
        public decimal TotalShippingTotal { get; set; }    // tổng shipping_total
        public decimal TotalVatTotal { get; set; }         // tổng vat_total
        public decimal TotalPayTotal { get; set; }         // tổng pay_total

        public decimal AverageOrderValue { get; set; }     // pay_total trung bình / đơn
    }

    public sealed class RevenueByPeriodRow
    {
        public DateTime PeriodDate { get; set; }
        public int OrderCount { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal ShippingTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal PayTotal { get; set; }
    }

    public sealed class RevenueByProviderRow
    {
        public string Provider { get; set; } = "";
        public int Method { get; set; }
        public int SuccessfulOrders { get; set; }
        public decimal TotalAmount { get; set; }

        // Label gọn cho UI / chart
        public string ProviderLabel =>
            string.IsNullOrWhiteSpace(Provider) ? "Khác" : Provider;

        public string MethodLabel
            => ProviderLabel == "COD"
                ? "COD"
                : Method switch
                {
                    1 => "ZaloPay",
                    2 => "VNPAY",
                    3 => "ZaloPay (App)",
                    _ => "Khác"
                };
    }

    public class RevenueReportViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string GroupBy { get; set; } = "day";

        public RevenueOverview Overview { get; set; } = new();
        public RevenueOverview PreviousOverview { get; set; } = new();
        public decimal RevenueChangePercent { get; set; }
        public decimal OrdersChangePercent { get; set; }

        public List<RevenueByPeriodRow> Rows { get; set; } = new();
        public List<RevenueByProviderRow> Providers { get; set; } = new();
    }

}
