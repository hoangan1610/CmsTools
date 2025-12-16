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
    MAX(CASE WHEN tp.can_view    = 1 THEN 1 ELSE 0 END) AS CanView,
    MAX(CASE WHEN tp.can_create  = 1 THEN 1 ELSE 0 END) AS CanCreate,
    MAX(CASE WHEN tp.can_update  = 1 THEN 1 ELSE 0 END) AS CanUpdate,
    MAX(CASE WHEN tp.can_delete  = 1 THEN 1 ELSE 0 END) AS CanDelete,

    MAX(CASE WHEN tp.can_publish  = 1 THEN 1 ELSE 0 END) AS CanPublish,
    MAX(CASE WHEN tp.can_schedule = 1 THEN 1 ELSE 0 END) AS CanSchedule,
    MAX(CASE WHEN tp.can_archive  = 1 THEN 1 ELSE 0 END) AS CanArchive,

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
                return new CmsTablePermission();

            return new CmsTablePermission
            {
                CanView = row.CanView == 1,
                CanCreate = row.CanCreate == 1,
                CanUpdate = row.CanUpdate == 1,
                CanDelete = row.CanDelete == 1,

                CanPublish = row.CanPublish == 1,
                CanSchedule = row.CanSchedule == 1,
                CanArchive = row.CanArchive == 1,

                RowFilter = row.HasRowFilter == 1 ? (string?)row.RowFilter : null
            };
        }

        public async Task<CmsTablePermission> GetTablePermissionAsync(int userId, string connectionName, string schemaName, string tableName)
        {
            const string sql = @"
SELECT
    MAX(CASE WHEN tp.can_view    = 1 THEN 1 ELSE 0 END) AS CanView,
    MAX(CASE WHEN tp.can_create  = 1 THEN 1 ELSE 0 END) AS CanCreate,
    MAX(CASE WHEN tp.can_update  = 1 THEN 1 ELSE 0 END) AS CanUpdate,
    MAX(CASE WHEN tp.can_delete  = 1 THEN 1 ELSE 0 END) AS CanDelete,

    MAX(CASE WHEN tp.can_publish  = 1 THEN 1 ELSE 0 END) AS CanPublish,
    MAX(CASE WHEN tp.can_schedule = 1 THEN 1 ELSE 0 END) AS CanSchedule,
    MAX(CASE WHEN tp.can_archive  = 1 THEN 1 ELSE 0 END) AS CanArchive,

    MAX(CASE WHEN tp.row_filter IS NULL THEN 0 ELSE 1 END) AS HasRowFilter,
    MAX(tp.row_filter) AS RowFilter
FROM dbo.tbl_cms_user_role ur
JOIN dbo.tbl_cms_role r ON r.id = ur.role_id
JOIN dbo.tbl_cms_table t ON 1=1
JOIN dbo.tbl_cms_connection c ON c.id = t.connection_id
LEFT JOIN dbo.tbl_cms_table_permission tp
    ON tp.role_id = ur.role_id AND tp.table_id = t.id
WHERE ur.user_id = @userId
  AND c.name = @connectionName
  AND t.schema_name = @schemaName
  AND t.table_name = @tableName;";

            using var conn = OpenMeta();
            var row = await conn.QueryFirstOrDefaultAsync(sql, new { userId, connectionName, schemaName, tableName });

            if (row == null) return new CmsTablePermission();

            return new CmsTablePermission
            {
                CanView = row.CanView == 1,
                CanCreate = row.CanCreate == 1,
                CanUpdate = row.CanUpdate == 1,
                CanDelete = row.CanDelete == 1,

                CanPublish = row.CanPublish == 1,
                CanSchedule = row.CanSchedule == 1,
                CanArchive = row.CanArchive == 1,

                RowFilter = row.HasRowFilter == 1 ? (string?)row.RowFilter : null
            };
        }


    }
}
