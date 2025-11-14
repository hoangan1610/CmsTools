namespace CmsTools.Models
{
    public sealed class CmsUserListItem
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public bool IsActive { get; set; }

        // Danh sách role, ghép chuỗi: "admin, editor"
        public string Roles { get; set; } = string.Empty;
    }
}
