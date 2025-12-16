using System.Threading.Tasks;
using CmsTools.Models;

namespace CmsTools.Services
{
    public interface ICmsPermissionService
    {
        Task<CmsTablePermission> GetTablePermissionAsync(int userId, int tableId);
        Task<CmsTablePermission> GetTablePermissionAsync(int userId, string connectionName, string schemaName, string tableName);
    }
}
