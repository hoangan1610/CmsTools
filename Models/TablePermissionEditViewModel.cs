using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class TablePermissionEditViewModel
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;

        public List<TablePermissionItem> Items { get; set; } = new();
    }
}
