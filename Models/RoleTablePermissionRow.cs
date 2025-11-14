namespace CmsTools.Models
{
    public sealed class RoleTablePermissionRow
    {
        public int TableId { get; set; }

        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }

        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }

        public string? RowFilter { get; set; }
    }
}
