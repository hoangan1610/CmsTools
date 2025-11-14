using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class RolePermissionViewModel
    {
        public IReadOnlyList<CmsRoleListItem> Roles { get; set; }
            = new List<CmsRoleListItem>();

        public int SelectedRoleId { get; set; }

        public IReadOnlyList<RoleTablePermissionRow> Tables { get; set; }
            = new List<RoleTablePermissionRow>();
    }
}
