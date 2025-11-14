namespace CmsTools.Models
{
    public sealed class RoleListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public int UserCount { get; set; }
        public int TablePermCount { get; set; }
    }
}
