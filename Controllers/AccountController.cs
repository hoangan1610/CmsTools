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
    public class AccountController : Controller
    {
        private readonly ICmsUserService _userService;

        public AccountController(ICmsUserService userService)
        {
            _userService = userService;
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
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError(string.Empty, "Username và password không được trống.");
                return View();
            }

            var user = await _userService.ValidateUserAsync(username, password);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Sai username hoặc password.");
                return View();
            }

            // ==== Lấy role & tạo claims ====
            var roles = await _userService.GetUserRoleNamesAsync(user.Id);

            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim("cms_user_id", user.Id.ToString())
    };

            var isAdmin = roles.Any(r => string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase));
            if (isAdmin)
            {
                claims.Add(new Claim("cms_is_admin", "1"));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            // ==== Redirect sau login ====
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");

            if (isAdmin)
            {
                // Admin → vào màn Schema quản trị
                return RedirectToAction("Index", "Schema");
            }

            // Non-admin → tạm thời cho nhảy vào danh mục sản phẩm (tableId = 1)
            // Sau này có thể query table_permission để tìm bảng đầu tiên mà user được xem.
            return RedirectToAction("List", "Data", new { tableId = 1 });
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
