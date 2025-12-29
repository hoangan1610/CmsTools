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

        private static string ResolvePk(CmsTableMeta table, IReadOnlyList<CmsColumnMeta> cols)
        {
            if (!string.IsNullOrWhiteSpace(table.PrimaryKey))
                return table.PrimaryKey!.Trim();

            var pkByMeta = cols.FirstOrDefault(c => c.IsPrimary && !string.IsNullOrWhiteSpace(c.ColumnName))?.ColumnName;
            if (!string.IsNullOrWhiteSpace(pkByMeta))
                return pkByMeta!;

            var idCol = cols.FirstOrDefault(c => string.Equals(c.ColumnName, "id", StringComparison.OrdinalIgnoreCase))?.ColumnName;
            if (!string.IsNullOrWhiteSpace(idCol))
                return idCol!;

            // fallback cuối cùng
            var first = cols.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.ColumnName))?.ColumnName;
            return !string.IsNullOrWhiteSpace(first) ? first! : "id";
        }

        private static object? NormalizeValue(CmsColumnMeta col, object? v)
        {
            if (v == null || v is DBNull) return null;

            var t = (col.DataType ?? "").ToLowerInvariant();

            // Fix bit đôi khi bị "0,1" do hidden + checkbox
            if (t.StartsWith("bit") && v is string s)
            {
                s = s.Trim();
                if (s.Contains(","))
                {
                    var last = s.Split(',', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                    s = last;
                }

                if (s == "1") return true;
                if (s == "0") return false;
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                if (s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }

            return v;
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

            if (cols == null || cols.Count == 0)
                throw new InvalidOperationException("No columns provided.");

            var colList = string.Join(", ", cols.Select(c => $"[{c.ColumnName}]"));
            var pk = ResolvePk(table, cols);
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
                    dp.Add(kv.Key, kv.Value);
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

        public async Task<IDictionary<string, object?>?> GetRowAsync(
            CmsConnectionMeta connMeta,
            CmsTableMeta table,
            IReadOnlyList<CmsColumnMeta> cols,
            string pkColumn,
            object pkValue)
        {
            if (cols == null || cols.Count == 0)
                throw new InvalidOperationException("No columns provided.");

            var colList = string.Join(", ", cols.Select(c => $"[{c.ColumnName}]"));

            var sql = $@"
SELECT {colList}
FROM {table.FullName}
WHERE [{pkColumn}] = @id;";

            using var conn = OpenTarget(connMeta);
            var row = await conn.QueryFirstOrDefaultAsync(sql, new { id = pkValue });

            return row == null ? null : (IDictionary<string, object?>)row;
        }

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
                if (string.IsNullOrWhiteSpace(col.ColumnName)) continue;

                // ✅ CHỈ update những cột có key trong values
                if (!values.ContainsKey(col.ColumnName))
                    continue;

                var v = NormalizeValue(col, values[col.ColumnName]);
                setClauses.Add($"[{col.ColumnName}] = @{col.ColumnName}");
                parameters.Add(col.ColumnName, v);
            }

            if (setClauses.Count == 0)
                return 0;

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
            if (editableCols == null)
                throw new ArgumentNullException(nameof(editableCols));
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            // ✅ Chỉ insert những cột CÓ key trong values
            var colsToInsert = editableCols
                .Where(c => !string.IsNullOrWhiteSpace(c.ColumnName) && values.ContainsKey(c.ColumnName))
                .ToList();

            var pk = ResolvePk(table, editableCols.Count > 0 ? editableCols : Array.Empty<CmsColumnMeta>());

            using var conn = OpenTarget(connMeta);

            // ✅ Nếu không có cột nào -> DEFAULT VALUES
            if (colsToInsert.Count == 0)
            {
                var sqlDefault = $@"
INSERT INTO {table.FullName}
OUTPUT INSERTED.[{(string.IsNullOrWhiteSpace(table.PrimaryKey) ? pk : table.PrimaryKey)}]
DEFAULT VALUES;";

                // note: pk ở đây chỉ để OUTPUT, bảng phải có identity pk
                return await conn.ExecuteScalarAsync<long>(sqlDefault);
            }

            // ✅ Validate required tại router (đỡ bị NULL lén)
            foreach (var col in colsToInsert)
            {
                var isRequired = !col.IsNullable && string.IsNullOrWhiteSpace(col.DefaultExpr);
                if (!isRequired) continue;

                var v = values[col.ColumnName];
                if (v == null || v is DBNull || (v is string s && string.IsNullOrWhiteSpace(s)))
                    throw new InvalidOperationException($"Required column '{col.ColumnName}' is missing/empty.");
            }

            var colNames = new List<string>();
            var paramNames = new List<string>();
            var parameters = new DynamicParameters();

            foreach (var col in colsToInsert)
            {
                colNames.Add($"[{col.ColumnName}]");
                paramNames.Add("@" + col.ColumnName);

                var v = NormalizeValue(col, values[col.ColumnName]);
                parameters.Add(col.ColumnName, v);
            }

            // ✅ OUTPUT đúng PK
            var outputPk = !string.IsNullOrWhiteSpace(table.PrimaryKey) ? table.PrimaryKey! : pk;

            var sql = $@"
INSERT INTO {table.FullName} ({string.Join(", ", colNames)})
OUTPUT INSERTED.[{outputPk}]
VALUES ({string.Join(", ", paramNames)});";

            var newId = await conn.ExecuteScalarAsync<long>(sql, parameters);
            return newId;
        }
    }
}
