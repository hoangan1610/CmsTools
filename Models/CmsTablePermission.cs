namespace CmsTools.Models
{
    public sealed class CmsTablePermission
    {
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }

        // ✅ thêm quyền riêng
        public bool CanPublish { get; set; }
        public bool CanSchedule { get; set; }
        public bool CanArchive { get; set; }

        public string? RowFilter { get; set; }
    }
}
