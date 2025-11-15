using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class PermissionTableListItem
    {
        public int TableId { get; set; }
        public string ConnectionName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public bool HasViewRole { get; set; }   // có bất kỳ role nào CanView
    }

    public sealed class PermissionTableIndexViewModel
    {
        public IReadOnlyList<PermissionTableListItem> Items { get; set; }
            = new List<PermissionTableListItem>();

        public IReadOnlyList<CmsConnectionMeta> Connections { get; set; }
            = new List<CmsConnectionMeta>();

        public int? ConnectionId { get; set; }
        public string? Search { get; set; }
    }
}
