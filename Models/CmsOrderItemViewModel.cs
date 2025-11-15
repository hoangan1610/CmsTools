namespace CmsTools.Models
{
    public sealed class CmsOrderItemViewModel
    {
        public long Id { get; set; }
        public long VariantId { get; set; }
        public long ProductId { get; set; }

        public string Sku { get; set; } = string.Empty;
        public string? NameVariant { get; set; }
        public decimal PriceVariant { get; set; }
        public string? ImageVariant { get; set; }

        public int Quantity { get; set; }
        public decimal LineSubtotal { get; set; }
    }
}
