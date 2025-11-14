using System.Collections.Generic;
using System.Threading.Tasks;
using CmsTools.Models;

namespace CmsTools.Services
{
    public sealed class CmsQueryResult
    {
        public IReadOnlyList<IDictionary<string, object?>> Rows { get; init; }
            = new List<IDictionary<string, object?>>();
        public int Total { get; init; }
    }

    public interface ICmsDbRouter
    {
        Task<CmsQueryResult> QueryAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> cols,
            string? where,
            IDictionary<string, object?>? parameters,
            int page,
            int pageSize);

        Task<IDictionary<string, object?>?> GetRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> cols,
            string pkColumn,
            object pkValue);

        Task<int> UpdateRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> editableCols,
            string pkColumn,
            object pkValue,
            IDictionary<string, object?> values);

        Task<long> InsertRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> editableCols,
            IDictionary<string, object?> values);
    }



}
