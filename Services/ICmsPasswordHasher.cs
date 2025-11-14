using System;

namespace CmsTools.Services
{
    public interface ICmsPasswordHasher
    {
        /// <summary>
        /// Hash password, trả về (hash, salt).
        /// </summary>
        (byte[] Hash, byte[] Salt) Hash(string password);

        /// <summary>
        /// Kiểm tra password nhập vào với hash + salt trong DB.
        /// </summary>
        bool Verify(string password, byte[] hash, byte[] salt);
    }
}
