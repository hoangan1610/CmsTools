using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class CmsOrderDetailViewModel
    {
        public long Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;

        public long UserInfoId { get; set; }
        public string? UserFullName { get; set; }
        public string? UserPhone { get; set; }

        public long? AddressId { get; set; }
        public string ShipName { get; set; } = string.Empty;
        public string ShipFullAddress { get; set; } = string.Empty;
        public string ShipPhone { get; set; } = string.Empty;
        public string? AddressLabel { get; set; }

        public byte Status { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal ShippingTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal PayTotal { get; set; }

        public string? PaymentStatus { get; set; }
        public string? PaymentProvider { get; set; }
        public string? PaymentRef { get; set; }

        public DateTime? PlacedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? CanceledAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public IReadOnlyList<CmsOrderItemViewModel> Items { get; set; }
            = Array.Empty<CmsOrderItemViewModel>();

        // Quick-link ra front-end
        public string? FrontendOrderUrl { get; set; }
        public string? FrontendTimelineUrl { get; set; }
    }
}
