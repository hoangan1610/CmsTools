using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class SchemaIndexViewModel
    {
        public IReadOnlyList<ConnectionWithTables> Items { get; init; }
            = new List<ConnectionWithTables>();

        public sealed class ConnectionWithTables
        {
            public CmsConnectionMeta Connection { get; init; } = default!;
            public IReadOnlyList<CmsTableMeta> Tables { get; init; }
                = new List<CmsTableMeta>();
        }
    }
}
