using System.Collections.Generic;

namespace CmsTools.Models
{
    public sealed class SchemaTreeViewModel
    {
        public IReadOnlyList<ConnectionNode> Items { get; init; }
            = new List<ConnectionNode>();

        public int? CurrentTableId { get; init; }

        public sealed class ConnectionNode
        {
            public CmsConnectionMeta Connection { get; init; } = default!;
            public IReadOnlyList<CmsTableMeta> Tables { get; init; }
                = new List<CmsTableMeta>();
        }
    }
}
