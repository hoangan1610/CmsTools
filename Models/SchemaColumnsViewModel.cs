using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class SchemaColumnsViewModel
    {
        public CmsTableMeta Table { get; set; } = default!;
        public IReadOnlyList<CmsColumnMeta> Columns { get; set; }
            = new List<CmsColumnMeta>();
    }
}
