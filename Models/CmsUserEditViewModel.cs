using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class CmsRoleOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class CmsUserEditViewModel
    {
        public int Id { get; set; }              // user id
        public string Username { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public bool IsActive { get; set; }

        // Role đã chọn cho user
        public List<int> SelectedRoleIds { get; set; } = new();

        // Tất cả role có trong hệ thống
        public IReadOnlyList<CmsRoleOption> Roles { get; set; }
            = new List<CmsRoleOption>();
    }
}
