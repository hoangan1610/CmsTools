using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class DataCreateViewModel
    {
        public CmsTableMeta Table { get; set; } = default!;
        public IReadOnlyList<CmsColumnMeta> Columns { get; set; } = default!;
    }
}
