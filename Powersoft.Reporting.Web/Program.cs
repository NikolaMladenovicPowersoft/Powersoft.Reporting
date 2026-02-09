using Microsoft.AspNetCore.Authentication.Cookies;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Auth;
using Powersoft.Reporting.Data.Central;
using Powersoft.Reporting.Data.Factories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var centralConnString = builder.Configuration.GetConnectionString("PSCentral");
if (!string.IsNullOrEmpty(centralConnString))
{
    builder.Services.AddSingleton<ICentralRepository>(sp => new CentralRepository(centralConnString));
    builder.Services.AddSingleton<IAuthenticationService>(sp => new AuthenticationService(centralConnString));
}

builder.Services.AddSingleton<ITenantRepositoryFactory, TenantRepositoryFactory>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "PowersoftReporting.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
