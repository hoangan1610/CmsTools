namespace CmsTools.Models
{
    // Dùng cho trang /Roles/Index
    public sealed class CmsRoleListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
    }

    // Dùng cho trang Create/Edit role
    public sealed class CmsRoleEditViewModel
    {
        public int Id { get; set; }          // Edit dùng, Create có thể bỏ qua
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }
}
