namespace CmsTools.Models
{
    public sealed class TablePermissionItem
    {
        public int TableId { get; set; }
        public string TableName { get; set; } = string.Empty;      // dbo.tbl_xxx
        public string DisplayName { get; set; } = string.Empty;    // tên hiển thị
        public string ConnectionName { get; set; } = string.Empty; // HAFoodDb local

        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        public string? RowFilter { get; set; }
    }
}
