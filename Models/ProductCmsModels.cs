using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class ProductInfoCmsDto
    {
        public long Id { get; set; }
        public long CategoryId { get; set; }
        public string? Tag { get; set; }
        public string? ProductKeyword { get; set; }
        public string BrandName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string ImageProduct { get; set; } = string.Empty;
        public string? Expiry { get; set; }
        public byte Status { get; set; }
        public long UserCreate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public string UserUpdate { get; set; } = string.Empty;
    }

    public sealed class ProductVariantCmsDto
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

    public sealed class ProductDetailCmsViewModel
    {
        public ProductInfoCmsDto Product { get; set; } = null!;
        public IReadOnlyList<ProductVariantCmsDto> Variants { get; set; } = Array.Empty<ProductVariantCmsDto>();
    }
}
