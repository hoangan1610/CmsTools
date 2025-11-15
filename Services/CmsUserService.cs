using System.Data;
using System.Threading.Tasks;
using CmsTools.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CmsTools.Services
{
    public sealed class CmsUserService : ICmsUserService
    {
        private readonly string _metaConn;
        private readonly ICmsPasswordHasher _hasher;

        public CmsUserService(IConfiguration cfg, ICmsPasswordHasher hasher)
        {
            _metaConn = cfg.GetConnectionString("CmsToolsDb")
                ?? throw new System.Exception("Missing connection string: CmsToolsDb");

            _hasher = hasher;
        }

        private IDbConnection OpenMeta() => new SqlConnection(_metaConn);

        // DTO nội bộ để nhận thêm PasswordHash + Salt
        private sealed class CmsUserRow
        {
            public int Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string? FullName { get; set; }
            public bool IsActive { get; set; }
            public byte[] PasswordHash { get; set; } = default!;
            public byte[] Salt { get; set; } = default!;

            // 👇 THÊM
            public bool IsAdmin { get; set; }
        }


        public async Task<CmsUser?> ValidateUserAsync(string username, string password)
        {
            const string sql = @"
SELECT 
    id,
    username,
    full_name     AS FullName,
    is_active     AS IsActive,
    password_hash AS PasswordHash,
    salt          AS Salt,
    is_admin      AS IsAdmin      -- 👈 THÊM
FROM dbo.tbl_cms_user
WHERE username = @username
  AND is_active = 1;";


            using var conn = OpenMeta();
            var row = await conn.QueryFirstOrDefaultAsync<CmsUserRow>(
                sql,
                new { username });

            if (row == null)
                return null;

            // Verify password bằng hasher mới
            var ok = _hasher.Verify(password, row.PasswordHash, row.Salt);
            if (!ok)
                return null;

            // Trả về CmsUser dùng cho claim / login
            return new CmsUser
            {
                Id = row.Id,
                Username = row.Username,
                FullName = row.FullName,
                IsActive = row.IsActive,
                IsAdmin = row.IsAdmin       // 👈 THÊM
            };

        }

        public async Task<IReadOnlyList<string>> GetUserRoleNamesAsync(int userId)
        {
            const string sql = @"
SELECT r.name
FROM dbo.tbl_cms_user_role ur
JOIN dbo.tbl_cms_role r ON r.id = ur.role_id
WHERE ur.user_id = @uid;";

            using var conn = OpenMeta();
            var rows = await conn.QueryAsync<string>(sql, new { uid = userId });
            return rows.ToList();
        }

    }
}
