namespace CmsTools.Models
{
    public sealed class CmsRoleItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Dùng cho UI: user hiện tại có role này hay chưa
        public bool IsChecked { get; set; }
    }
}
