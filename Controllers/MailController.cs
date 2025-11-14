using System.Threading.Tasks;
using CmsTools.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CmsTools.Controllers
{
    [Authorize]
    public class MailController : Controller
    {
        private readonly IEmailSender _email;
        private readonly SmtpOptions _opt;

        public MailController(IEmailSender email, IOptions<SmtpOptions> opt)
        {
            _email = email;
            _opt = opt.Value;
        }

        [HttpGet]
        public IActionResult Test()
        {
            ViewBag.DefaultTo = _opt.DefaultTo;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Test(string to, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(to))
                to = _opt.DefaultTo;

            if (string.IsNullOrWhiteSpace(subject))
                subject = "[CMS_Tools] Test email";

            if (string.IsNullOrWhiteSpace(body))
                body = "Đây là email test gửi từ CMS_Tools.";

            try
            {
                await _email.SendAsync(to, subject, body);
                ViewBag.Message = $"Đã gửi email tới {to}.";
            }
            catch (System.Exception ex)
            {
                ViewBag.Message = "Lỗi gửi mail: " + ex.Message;
            }

            ViewBag.DefaultTo = _opt.DefaultTo;
            return View();
        }
    }
}
