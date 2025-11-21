namespace CmsTools.Models
{
    public sealed class AdminReviewViewModel
    {
        public long Id { get; set; }
        public long Product_Id { get; set; }
        public long? Variant_Id { get; set; }
        public long User_Info_Id { get; set; }

        public string? User_Name { get; set; }
        public string? Product_Name { get; set; }

        public byte Rating { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }

        public bool Has_Image { get; set; }
        public bool Is_Verified_Purchase { get; set; }

        public byte Status { get; set; }
        public DateTime Created_At { get; set; }
        public DateTime? Updated_At { get; set; }
    }
}
