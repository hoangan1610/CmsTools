using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class DataEditViewModel
    {
        public CmsTableMeta Table { get; set; } = default!;
        public IReadOnlyList<CmsColumnMeta> Columns { get; set; } = default!;

        // Giá trị hiện tại của row (key = column_name)
        public IDictionary<string, object?> Values { get; set; } = default!;

        public string PrimaryKeyColumn { get; set; } = default!;
        public object PrimaryKeyValue { get; set; } = default!;
    }
}
