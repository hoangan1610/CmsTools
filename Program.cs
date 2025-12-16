using CmsTools.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ========== ĐĂNG KÝ DỊCH VỤ ==========
// Hasher dùng cho password CMS
builder.Services.AddSingleton<ICmsPasswordHasher, CmsPasswordHasher>();

// ===== cấu hình SmtpOptions =====
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));

// Email sender
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// MVC
builder.Services.AddControllersWithViews();

// Services CMS
builder.Services.AddScoped<ICmsMetaService, CmsMetaService>();
builder.Services.AddScoped<ICmsDbRouter, CmsDbRouter>();
builder.Services.AddScoped<ICmsUserService, CmsUserService>();
builder.Services.AddScoped<ICmsPermissionService, CmsPermissionService>();
builder.Services.AddScoped<ICmsAuditLogger, CmsAuditLogger>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient();
// Login / action audit
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
// Auth: Cookie
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CmsUser", policy =>
    {
        policy.RequireAuthenticatedUser();
        // yêu cầu có cms_user_id (đúng hệ CMS của bạn)
        policy.RequireClaim("cms_user_id");
    });

    options.AddPolicy("CmsAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("cms_is_admin", "1");
    });
});




// ========== BUILD APP ==========

var app = builder.Build();

// ========== MIDDLEWARE PIPELINE ==========

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();   // 👈 phải trước UseAuthorization
app.UseAuthorization();

// Route mặc định: có thể tuỳ chỉnh controller/action tùy ý
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


// ========== RUN ==========

app.Run();
