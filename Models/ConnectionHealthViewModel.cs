namespace CmsTools.Models
{
    public sealed class ConnectionHealthViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public bool IsOk { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
