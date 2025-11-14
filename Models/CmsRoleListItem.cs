namespace CmsTools.Models
{
    public sealed class CmsRoleListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        // thêm 2 field mới
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
    }

    public sealed class CmsRoleEditViewModel
    {
        public int? Id { get; set; }        // null = create, >0 = edit
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
