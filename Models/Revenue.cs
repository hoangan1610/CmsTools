using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    // =========================
    // Revenue recognized (delivered)
    // =========================
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

    // =========================
    // Cash-in (cashflow)
    // - Online: paid_at (payment success)
    // - COD: delivered/received_confirmed
    // =========================
    public sealed class CashflowOverview
    {
        public decimal TotalCashIn { get; set; }
        public int TotalOrders { get; set; }

        public decimal OnlineCashIn { get; set; }
        public int OnlineOrders { get; set; }

        public decimal CodCashIn { get; set; }
        public int CodOrders { get; set; }
    }

    public sealed class CashflowByPeriodRow
    {
        public DateTime PeriodDate { get; set; }

        public decimal OnlineAmount { get; set; }
        public int OnlineOrders { get; set; }

        public decimal CodAmount { get; set; }
        public int CodOrders { get; set; }

        public decimal TotalAmount { get; set; }
        public int TotalOrders { get; set; }
    }

    public sealed class CashflowByProviderRow
    {
        public string? ProviderLabel { get; set; }
        public string? MethodLabel { get; set; }
        public decimal TotalAmount { get; set; }
        public int SuccessfulOrders { get; set; }
    }

    // =========================
    // Pipeline snapshot (open orders)
    // =========================
    public sealed class PipelineSnapshot
    {
        public DateTime AsOfExcl { get; set; }

        public int OpenOrders { get; set; }
        public decimal OpenAmount { get; set; }

        public int CodOutstandingOrders { get; set; }
        public decimal CodOutstandingAmount { get; set; }

        public int OnlinePendingOrders { get; set; }
        public decimal OnlinePendingAmount { get; set; }

        public int OnlinePrepaidOrders { get; set; }
        public decimal OnlinePrepaidAmount { get; set; }
    }

    public sealed class PipelineByStatusRow
    {
        public byte Status { get; set; }
        public int OrderCount { get; set; }
        public decimal Amount { get; set; }

        public decimal CodAmount { get; set; }
        public decimal OnlinePendingAmount { get; set; }
        public decimal OnlinePrepaidAmount { get; set; }
    }

    // =========================
    // ViewModel (Revenue page)
    // =========================
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

        // ===== Cash-in (dòng tiền) =====
        public CashflowOverview CashflowOverview { get; set; } = new CashflowOverview();
        public List<CashflowByPeriodRow> CashflowRows { get; set; } = new List<CashflowByPeriodRow>();
        public List<CashflowByProviderRow> CashflowProviders { get; set; } = new List<CashflowByProviderRow>();

        // ===== Pipeline snapshot (tiền đang nằm ở đơn chưa hoàn tất) =====
        public PipelineSnapshot Pipeline { get; set; } = new PipelineSnapshot();
        public List<PipelineByStatusRow> PipelineByStatus { get; set; } = new List<PipelineByStatusRow>();
    }

    // =========================
    // AI Insight
    // =========================
    public sealed class RevenueAiInsightResult
    {
        public string Summary { get; set; } = "";
        public List<string> Highlights { get; set; } = new();
        public List<string> Alerts { get; set; } = new();

        // optional: để debug/trace khi cần
        public string? Raw { get; set; }
    }

    // =========================
    // Reorder Suggest
    // =========================
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
