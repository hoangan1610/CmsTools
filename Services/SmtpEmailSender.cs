using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace CmsTools.Services
{
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opt;

        public SmtpEmailSender(IOptions<SmtpOptions> opt)
        {
            _opt = opt.Value;
        }

        public async Task SendAsync(string to, string subject, string bodyHtml)
        {
            // Fallback nếu caller không truyền to
            if (string.IsNullOrWhiteSpace(to))
            {
                to = _opt.DefaultTo;
            }

            using var client = new SmtpClient(_opt.Host, _opt.Port)
            {
                EnableSsl = _opt.EnableSsl,
                Credentials = new NetworkCredential(_opt.User, _opt.Password)
            };

            var from = string.IsNullOrWhiteSpace(_opt.DefaultFrom)
                ? _opt.User
                : _opt.DefaultFrom;

            using var msg = new MailMessage(from, to)
            {
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            await client.SendMailAsync(msg);
        }
    }
}
