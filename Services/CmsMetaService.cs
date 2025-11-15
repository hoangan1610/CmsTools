using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Services
{
    public sealed class CmsMetaService : ICmsMetaService
    {
        private readonly string _metaConn;

        public CmsMetaService(IConfiguration cfg)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new System.Exception("Missing connection string: CmsToolsDb");
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        public async Task<CmsTableMeta?> GetTableAsync(int tableId)
        {
            const string sql = @"
SELECT id,
       connection_id AS ConnectionId,
       schema_name   AS SchemaName,
       table_name    AS TableName,
       display_name  AS DisplayName,
       primary_key   AS PrimaryKey,
       is_view       AS IsView,
       is_enabled    AS IsEnabled,
row_filter    AS RowFilter ,
custom_detail_url AS CustomDetailUrl
FROM dbo.tbl_cms_table
WHERE id = @id AND is_enabled = 1;";

            using var conn = OpenMeta();
            return await conn.QueryFirstOrDefaultAsync<CmsTableMeta>(sql, new { id = tableId });
        }

        public async Task<IReadOnlyList<CmsColumnMeta>> GetColumnsAsync(int tableId, bool forList)
        {
            var sql = @"
SELECT id,
       table_id     AS TableId,
       column_name  AS ColumnName,
       display_name AS DisplayName,
       data_type    AS DataType,
       is_nullable  AS IsNullable,
       is_primary   AS IsPrimary,
       is_list      AS IsList,
       is_editable  AS IsEditable,
       is_filter    AS IsFilter,
       width,
       format,
default_expr AS DefaultExpr 
FROM dbo.tbl_cms_column
WHERE table_id = @tableId";

            if (forList)
                sql += " AND is_list = 1";

            sql += " ORDER BY sort_order, column_name;";

            using var conn = OpenMeta();
            var rows = await conn.QueryAsync<CmsColumnMeta>(sql, new { tableId });
            return rows.ToList();
        }

        public async Task<CmsConnectionMeta?> GetConnectionAsync(int connectionId)
        {
            const string sql = @"
SELECT id,
       name,
       provider,
       conn_string AS ConnString,
       is_active AS IsActive
FROM dbo.tbl_cms_connection
WHERE id = @id AND is_active = 1;";

            using var conn = OpenMeta();
            return await conn.QueryFirstOrDefaultAsync<CmsConnectionMeta>(sql, new { id = connectionId });
        }

        public async Task<IReadOnlyList<CmsConnectionMeta>> GetConnectionsAsync()
        {
            const string sql = @"
SELECT id,
       name,
       provider,
       conn_string AS ConnString,
       is_active
FROM dbo.tbl_cms_connection
WHERE is_active = 1
ORDER BY sort_order, name;";

            using var conn = OpenMeta();
            var rows = await conn.QueryAsync<CmsConnectionMeta>(sql);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<CmsTableMeta>> GetTablesByConnectionAsync(int connectionId)
        {
            const string sql = @"
SELECT id,
       connection_id AS ConnectionId,
       schema_name   AS SchemaName,
       table_name    AS TableName,
       display_name  AS DisplayName,
       primary_key   AS PrimaryKey,
       is_view       AS IsView,
       is_enabled    AS IsEnabled,
       row_filter    AS RowFilter,
custom_detail_url AS CustomDetailUrl 
FROM dbo.tbl_cms_table
WHERE connection_id = @connectionId
  AND is_enabled = 1
ORDER BY sort_order, schema_name, table_name;";

            using var conn = OpenMeta();
            var rows = await conn.QueryAsync<CmsTableMeta>(sql, new { connectionId });
            return rows.ToList();
        }

    }


}
