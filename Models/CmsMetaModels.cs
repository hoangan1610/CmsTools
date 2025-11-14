using System;

namespace CmsTools.Models
{
    public sealed class CmsConnectionMeta
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Provider { get; set; } = default!;   // 'mssql'
        public string ConnString { get; set; } = default!;
        public bool IsActive { get; set; }
    }

    public sealed class CmsTableMeta
    {
        public int Id { get; set; }
        public int ConnectionId { get; set; }
        public string SchemaName { get; set; } = "dbo";
        public string TableName { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? PrimaryKey { get; set; }
        public bool IsView { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? RowFilter { get; set; }
        public string FullName => $"[{SchemaName}].[{TableName}]";
    }

    public sealed class CmsColumnMeta
    {
        public int Id { get; set; }
        public int TableId { get; set; }
        public string ColumnName { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string DataType { get; set; } = default!;
        public bool IsNullable { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsList { get; set; }
        public bool IsEditable { get; set; }
        public bool IsFilter { get; set; }
        public int? Width { get; set; }
        public string? Format { get; set; }
        public string? DefaultExpr { get; set; }
    }
}
