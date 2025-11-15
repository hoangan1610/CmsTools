namespace CmsTools.Models
{
    public sealed class CmsTableListItem
    {
        public int Id { get; set; }

        public int ConnectionId { get; set; }
        public string ConnectionName { get; set; } = string.Empty;

        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
        public string? PrimaryKey { get; set; }

        public string? CustomDetailUrl { get; set; }
    }
}
