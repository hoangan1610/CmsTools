using System.Data;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Services
{
    public sealed class CmsPermissionService : ICmsPermissionService
    {
        private readonly string _metaConn;

        public CmsPermissionService(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new System.Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        public async Task<CmsTablePermission> GetTablePermissionAsync(int userId, int tableId)
        {
            const string sql = @"
SELECT
    MAX(CASE WHEN tp.can_view   = 1 THEN 1 ELSE 0 END) AS CanView,
    MAX(CASE WHEN tp.can_create = 1 THEN 1 ELSE 0 END) AS CanCreate,
    MAX(CASE WHEN tp.can_update = 1 THEN 1 ELSE 0 END) AS CanUpdate,
    MAX(CASE WHEN tp.can_delete = 1 THEN 1 ELSE 0 END) AS CanDelete,
    MAX(CASE WHEN tp.row_filter IS NULL THEN 0 ELSE 1 END) AS HasRowFilter,
    MAX(tp.row_filter) AS RowFilter
FROM dbo.tbl_cms_user_role ur
JOIN dbo.tbl_cms_table_permission tp
    ON tp.role_id = ur.role_id
WHERE ur.user_id = @userId
  AND tp.table_id = @tableId;";

            using var conn = OpenMeta();
            var row = await conn.QueryFirstOrDefaultAsync(sql, new { userId, tableId });

            if (row == null)
            {
                // mặc định: không có quyền gì
                return new CmsTablePermission
                {
                    CanView = false,
                    CanCreate = false,
                    CanUpdate = false,
                    CanDelete = false,
                    RowFilter = null
                };
            }

            // dynamic -> strong type
            bool canView = row.CanView == 1;
            bool canCreate = row.CanCreate == 1;
            bool canUpdate = row.CanUpdate == 1;
            bool canDelete = row.CanDelete == 1;
            string? rowFilter = row.HasRowFilter == 1 ? (string?)row.RowFilter : null;

            return new CmsTablePermission
            {
                CanView = canView,
                CanCreate = canCreate,
                CanUpdate = canUpdate,
                CanDelete = canDelete,
                RowFilter = rowFilter
            };
        }
    }
}
