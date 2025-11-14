using System.Threading.Tasks;
using CmsTools.Models;

namespace CmsTools.Services
{
    public interface ICmsUserService
    {
        Task<CmsUser?> ValidateUserAsync(string username, string password);

        Task<IReadOnlyList<string>> GetUserRoleNamesAsync(int userId);
    }

}
