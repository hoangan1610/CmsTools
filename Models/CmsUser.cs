namespace CmsTools.Models
{
    public sealed class CmsUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = default!;
        public string? FullName { get; set; }
        public bool IsActive { get; set; }

        public bool IsAdmin { get; set; }
    }
}
