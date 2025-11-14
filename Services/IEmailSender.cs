using System.Threading.Tasks;

namespace CmsTools.Services
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string bodyHtml);
    }
}
