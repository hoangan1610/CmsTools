using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class TablePermissionEditViewModel
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;

        public List<TablePermissionItem> Items { get; set; }
            = new List<TablePermissionItem>();

        // ===== MỚI: danh sách role có thể copy từ đó =====
        public IReadOnlyList<CmsRoleOption> CopyRoles { get; set; }
            = new List<CmsRoleOption>();

        // Role nguồn vừa được chọn để copy (nếu có)
        public int? CopyFromRoleId { get; set; }
    }
}
