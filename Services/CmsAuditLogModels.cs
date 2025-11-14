using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class CmsAuditLogListItem
    {
        public long Id { get; set; }
        public int? UserId { get; set; }
        public string? Username { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? PrimaryKeyColumn { get; set; }
        public string? PrimaryKeyValue { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public string TableFullName =>
            string.IsNullOrEmpty(SchemaName) ? TableName : $"{SchemaName}.{TableName}";
    }

    public sealed class CmsAuditLogListViewModel
    {
        public IReadOnlyList<CmsAuditLogListItem> Items { get; set; }
            = Array.Empty<CmsAuditLogListItem>();

        public int Page { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }

        // filter
        public string? Operation { get; set; }
        public string? TableFilter { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }

    public sealed class CmsAuditLogDetailModel
    {
        public long Id { get; set; }
        public int? UserId { get; set; }
        public string? Username { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? PrimaryKeyColumn { get; set; }
        public string? PrimaryKeyValue { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? OldValuesJson { get; set; }
        public string? NewValuesJson { get; set; }
    }
}
