using System;
using System.Security.Cryptography;
using System.Text;

namespace CmsTools.Services
{
    public sealed class CmsPasswordHasher : ICmsPasswordHasher
    {
        // Có thể chỉnh số vòng nếu muốn (đang dùng PBKDF2).
        private const int Iterations = 100_000;
        private const int SaltSize = 16;   // 128 bit
        private const int HashSize = 32;   // 256 bit

        public (byte[] Hash, byte[] Salt) Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password must not be empty.", nameof(password));

            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            var hash = Derive(password, salt);
            return (hash, salt);
        }

        public bool Verify(string password, byte[] hash, byte[] salt)
        {
            if (hash == null || hash.Length == 0 || salt == null || salt.Length == 0)
                return false;

            var computed = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(computed, hash);
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256);

            return pbkdf2.GetBytes(HashSize);
        }
    }
}
