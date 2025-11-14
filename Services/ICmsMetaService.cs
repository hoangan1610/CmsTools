using System.Collections.Generic;
using System.Threading.Tasks;
using CmsTools.Models;

namespace CmsTools.Services
{
    public interface ICmsMetaService
    {
        Task<CmsTableMeta?> GetTableAsync(int tableId);
        Task<IReadOnlyList<CmsColumnMeta>> GetColumnsAsync(int tableId, bool forList);
        Task<CmsConnectionMeta?> GetConnectionAsync(int connectionId);

        Task<IReadOnlyList<CmsConnectionMeta>> GetConnectionsAsync();
        Task<IReadOnlyList<CmsTableMeta>> GetTablesByConnectionAsync(int connectionId);
    }
}
