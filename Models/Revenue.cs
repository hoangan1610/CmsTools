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
        public string ProviderLabel { get; set; } = "";
        public string MethodLabel { get; set; } = "";
        public int SuccessfulOrders { get; set; }
        public decimal TotalAmount { get; set; }
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

        public RevenueAiInsightResult? Ai { get; set; }
    }

    public sealed class RevenueAiInsightResult
    {
        public string Summary { get; set; } = "";
        public List<string> Highlights { get; set; } = new();
        public List<string> Alerts { get; set; } = new();

        // optional: để debug/trace khi cần
        public string? Raw { get; set; }
    }


    public sealed class ReorderSuggestRow
    {
        public long VariantId { get; set; }
        public string Sku { get; set; } = "";
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string VariantName { get; set; } = "";

        public int Stock { get; set; }
        public long SoldQty { get; set; }
        public int OrderCount { get; set; }
        public decimal SoldAmount { get; set; }

        public decimal AvgQtyPerDay { get; set; }
        public decimal? DaysCover { get; set; }

        public decimal ForecastQty { get; set; }
        public decimal TargetStock { get; set; }
        public int SuggestQty { get; set; }

        public decimal RetailPrice { get; set; }
        public decimal CostPrice { get; set; }
        public decimal FinishedCost { get; set; }

        public string RiskLevel { get; set; } = "";
    }

    public sealed class ReorderSuggestViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public int HorizonDays { get; set; } = 30;
        public decimal SafetyFactor { get; set; } = 1.20m;
        public int TopN { get; set; } = 200;

        public bool OnlyActive { get; set; } = true;
        public bool IncludeZeroSales { get; set; } = false;

        public List<ReorderSuggestRow> Rows { get; set; } = new();
    }
}
