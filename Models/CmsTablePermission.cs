namespace CmsTools.Models
{
    public sealed class CmsTablePermission
    {
        public bool CanView { get; init; }
        public bool CanCreate { get; init; }
        public bool CanUpdate { get; init; }
        public bool CanDelete { get; init; }

        // Có thể override row_filter của bảng theo role
        public string? RowFilter { get; init; }
    }
}
