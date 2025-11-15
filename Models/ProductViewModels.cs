using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    // Bản ghi product đọc từ tbl_product_info
    public sealed class ProductInfoRow
    {
        public long Id { get; set; }
        public long CategoryId { get; set; }
        public string BrandName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public string? ProductKeyword { get; set; }
        public string Detail { get; set; } = string.Empty;
        public string ImageProduct { get; set; } = string.Empty;
        public byte Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // Bản ghi variant đọc từ tbl_product_variant
    public sealed class ProductVariantRow
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string? Name { get; set; }
        public string? Image { get; set; }
        public string? MetaData { get; set; }
        public string Sku { get; set; } = string.Empty;
        public int? Weight { get; set; }
        public decimal CostPrice { get; set; }
        public decimal FinishedCost { get; set; }
        public decimal WholesalePrice { get; set; }
        public decimal RetailPrice { get; set; }
        public int Stock { get; set; }
        public byte Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // ViewModel cho /Products/Detail
    public sealed class ProductDetailViewModel
    {
        public ProductInfoRow Product { get; set; } = new ProductInfoRow();
        public IReadOnlyList<ProductVariantRow> Variants { get; set; } = Array.Empty<ProductVariantRow>();
    }

    // ViewModel dùng cho Create/Edit variant
    public sealed class ProductVariantEditViewModel
    {
        public long ProductId { get; set; }
        public long? Id { get; set; }   // null = create

        public string ProductName { get; set; } = string.Empty;

        public string? Name { get; set; }
        public string? Image { get; set; }
        public string? MetaData { get; set; }
        public string Sku { get; set; } = string.Empty;
        public int? Weight { get; set; }
        public decimal? CostPrice { get; set; }
        public decimal? FinishedCost { get; set; }
        public decimal? WholesalePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public int? Stock { get; set; }
        public byte Status { get; set; } = 1;
    }
}
