using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CmsTools.Services
{
    public sealed class CmsDbRouter : ICmsDbRouter
    {
        private IDbConnection OpenTarget(CmsConnectionMeta meta)
        {
            if (!string.Equals(meta.Provider, "mssql", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Provider not supported: {meta.Provider}");

            return new SqlConnection(meta.ConnString);
        }

        public async Task<CmsQueryResult> QueryAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> cols,
            string? where,
            IDictionary<string, object?>? parameters,
            int page,
            int pageSize)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var colList = string.Join(", ",
                cols.Select(c => $"[{c.ColumnName}]"));

            var pk = !string.IsNullOrWhiteSpace(table.PrimaryKey)
                ? table.PrimaryKey!
                : cols.First().ColumnName;

            var whereClause = string.IsNullOrWhiteSpace(where) ? "1 = 1" : where;

            var sql = $@"
SELECT {colList}
FROM {table.FullName}
WHERE {whereClause}
ORDER BY [{pk}] DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

SELECT COUNT(1)
FROM {table.FullName}
WHERE {whereClause};";

            var skip = (page - 1) * pageSize;

            var dp = new DynamicParameters();
            dp.Add("skip", skip);
            dp.Add("take", pageSize);

            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    dp.Add(kv.Key, kv.Value);
                }
            }

            using var conn = OpenTarget(connMeta);
            using var multi = await conn.QueryMultipleAsync(sql, dp);

            var rows = (await multi.ReadAsync())
                .Select(r => (IDictionary<string, object?>)r)
                .ToList();

            var total = await multi.ReadFirstAsync<int>();

            return new CmsQueryResult
            {
                Rows = rows,
                Total = total
            };
        }

        // 👇 Lấy 1 row theo PK
        public async Task<IDictionary<string, object?>?> GetRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> cols,
            string pkColumn,
            object pkValue)
        {
            var colList = string.Join(", ",
                cols.Select(c => $"[{c.ColumnName}]"));

            var sql = $@"
SELECT {colList}
FROM {table.FullName}
WHERE [{pkColumn}] = @id;";

            using var conn = OpenTarget(connMeta);
            var row = await conn.QueryFirstOrDefaultAsync(sql, new { id = pkValue });

            return row == null ? null : (IDictionary<string, object?>)row;
        }

        // 👇 Update row theo PK
        public async Task<int> UpdateRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> editableCols,
            string pkColumn,
            object pkValue,
            IDictionary<string, object?> values)
        {
            if (editableCols == null || editableCols.Count == 0)
                return 0;

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", pkValue);

            foreach (var col in editableCols)
            {
                setClauses.Add($"[{col.ColumnName}] = @{col.ColumnName}");
                values.TryGetValue(col.ColumnName, out var v);
                parameters.Add(col.ColumnName, v);
            }

            var sql = $@"
UPDATE {table.FullName}
SET {string.Join(", ", setClauses)}
WHERE [{pkColumn}] = @id;";

            using var conn = OpenTarget(connMeta);
            return await conn.ExecuteAsync(sql, parameters);
        }

        public async Task<long> InsertRowAsync(
    CmsConnectionMeta connMeta,
    CmsTableMeta table,
    IReadOnlyList<CmsColumnMeta> editableCols,
    IDictionary<string, object?> values)
        {
            if (editableCols == null || editableCols.Count == 0)
                return 0;

            var colNames = new List<string>();
            var paramNames = new List<string>();
            var parameters = new DynamicParameters();

            foreach (var col in editableCols)
            {
                colNames.Add($"[{col.ColumnName}]");
                paramNames.Add("@" + col.ColumnName);

                values.TryGetValue(col.ColumnName, out var v);
                parameters.Add(col.ColumnName, v);
            }

            // Giả sử PK là identity, dùng OUTPUT INSERTED.[pk]
            var pk = !string.IsNullOrWhiteSpace(table.PrimaryKey)
                ? table.PrimaryKey!
                : editableCols.First().ColumnName;

            var sql = $@"
INSERT INTO {table.FullName} ({string.Join(", ", colNames)})
OUTPUT INSERTED.[{pk}]
VALUES ({string.Join(", ", paramNames)});";

            using var conn = OpenTarget(connMeta);
            var newId = await conn.ExecuteScalarAsync<long>(sql, parameters);
            return newId;
        }

    }
}
