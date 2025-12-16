using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class CmsArticleListItemVm
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public byte Status { get; set; }
        public DateTime? Published_At_Utc { get; set; }
        public DateTime? Scheduled_At_Utc { get; set; } // NEW
        public bool Is_Featured { get; set; }
        public int? Featured_Order { get; set; }
        public DateTime Updated_At_Utc { get; set; }
    }

    public sealed class CmsArticleEditVm
    {
        public long Id { get; set; }

        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";

        public string? Excerpt { get; set; }
        public string? Cover_Image_Url { get; set; }

        public string? Content_Html { get; set; }
        public string Content_Json { get; set; } = "{\"time\":0,\"blocks\":[],\"version\":\"2\"}";

        public byte Status { get; set; } // 0 draft, 1 published, 2 scheduled, 3 archived
        public DateTime? Published_At_Utc { get; set; }
        public DateTime? Scheduled_At_Utc { get; set; }

        public bool Is_Featured { get; set; }
        public int? Featured_Order { get; set; }

        public string? Meta_Title { get; set; }
        public string? Meta_Description { get; set; }
        public string? Og_Image_Url { get; set; }
        public string? Canonical_Url { get; set; }

        public List<long> Category_Ids { get; set; } = new();
        public List<long> Tag_Ids { get; set; } = new();
        public List<CmsArticleCardVm> Cards { get; set; } = new();

        public List<CmsOptionVm> Category_Options { get; set; } = new();
        public List<CmsOptionVm> Tag_Options { get; set; } = new();

        public DateTime? Scheduled_At_Vn { get; set; }

        // base64 row_version
        public string? Concurrency_Token { get; set; }

        public DateTime? Updated_At_Utc { get; set; }
        public DateTime? Last_Autosave_At_Utc { get; set; }
    }

    public sealed class CmsArticleCardVm
    {
        public long Product_Id { get; set; }
        public long? Variant_Id { get; set; }
        public int Sort_Order { get; set; }

        public string Product_Name { get; set; } = "";
        public string? Product_Image { get; set; }
        public string? Variant_Name { get; set; }
        public decimal? Retail_Price { get; set; }
        public int? Stock { get; set; }
    }

    public sealed class CmsOptionVm
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }
}
