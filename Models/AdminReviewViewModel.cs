using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class AdminReviewImageViewModel
    {
        public long Id { get; set; }
        public string Image_Url { get; set; } = "";
    }

    public sealed class AdminReviewViewModel
    {
        public long Id { get; set; }
        public long Product_Id { get; set; }
        public long? Variant_Id { get; set; }
        public long User_Info_Id { get; set; }

        public string User_Name { get; set; }
        public string Product_Name { get; set; }

        public byte Rating { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }

        public bool Has_Image { get; set; }
        public bool Is_Verified_Purchase { get; set; }
        public byte Status { get; set; }
        public DateTime Created_At { get; set; }
        public DateTime Updated_At { get; set; }

        // 🔹 mới: ẩn/hiện review trên web
        public bool Is_Hidden { get; set; }

        // reply
        public string? Reply_Content { get; set; }
        public DateTime? Reply_Created_At { get; set; }
        public long? Reply_Admin_User_Id { get; set; }

        // 🔹 mới: tên admin (tạm để null, sau này join bảng user CMS)
        public string? Reply_Admin_Name { get; set; }

        // 🔹 danh sách ảnh đánh giá
        public List<AdminReviewImageViewModel> Images { get; set; } = new();
    }
}
