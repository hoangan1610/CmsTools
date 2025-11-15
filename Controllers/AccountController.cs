using System.Security.Claims;
using System.Threading.Tasks;
using CmsTools.Models;
using CmsTools.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CmsTools.Controllers
{
    public sealed class AccountController : Controller
    {
        private readonly ICmsUserService _userService;
        private readonly IAuditLogger _auditLogger;

        public AccountController(ICmsUserService userService, IAuditLogger auditLogger)
        {
            _userService = userService;
            _auditLogger = auditLogger;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            // tuỳ ICmsUserService của bạn, mình giả sử có ValidateUserAsync
            var user = await _userService.ValidateUserAsync(model.Username, model.Password);

            if (user == null)
            {
                await _auditLogger.LogLoginAsync(
                    HttpContext,
                    username: model.Username,
                    isSuccess: false,
                    message: "Invalid username or password"
                );

                ModelState.AddModelError("", "Sai username hoặc password.");
                return View(model);
            }

            // === Đăng nhập thành công (cookie) ===
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("cms_user_id", user.Id.ToString()),
                // nếu có flag is_admin:
                new Claim("cms_is_admin", user.IsAdmin ? "1" : "0")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            await _auditLogger.LogLoginAsync(
                HttpContext,
                username: user.Username,
                isSuccess: true,
                message: "Login success"
            );

            return RedirectToLocal(returnUrl);
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();   // Views/Account/AccessDenied.cshtml
        }
    }
}
