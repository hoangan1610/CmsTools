using System.Collections.Generic;
using System.Threading.Tasks;
using CmsTools.Models;

namespace CmsTools.Services
{
    public interface ICmsAuditLogger
    {
        Task LogAsync(
            int? userId,
            string operation,                     // "CREATE", "UPDATE", "SET_STATUS"...
            CmsTableMeta table,
            string pkColumn,
            object pkValue,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues
        );
    }
}
