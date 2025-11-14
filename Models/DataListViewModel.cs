using System;
using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class DataListViewModel
    {
        public CmsTableMeta Table { get; set; } = default!;

        // Cột hiển thị ở grid
        public IReadOnlyList<CmsColumnMeta> Columns { get; set; }
            = Array.Empty<CmsColumnMeta>();

        // Cột có cho phép filter (is_filter = 1)
        public IReadOnlyList<CmsColumnMeta> FilterColumns { get; set; }
            = Array.Empty<CmsColumnMeta>();

        public IReadOnlyList<IDictionary<string, object?>> Rows { get; set; }
            = Array.Empty<IDictionary<string, object?>>();

        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        public int TotalPages =>
            PageSize <= 0 ? 1 : (int)Math.Ceiling((double)Total / PageSize);

        // Giá trị filter hiện tại (key = column_name)
        public Dictionary<string, string> Filters { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public CmsTablePermission Permission { get; set; } = new CmsTablePermission();
    }
}
