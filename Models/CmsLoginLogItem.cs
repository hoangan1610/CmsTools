using System;

namespace CmsTools.Models
{
    public sealed class CmsLoginLogItem
    {
        public int Id { get; set; }
        public int? CmsUserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
