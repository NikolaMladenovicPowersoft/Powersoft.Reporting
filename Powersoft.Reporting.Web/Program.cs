using Microsoft.AspNetCore.Authentication.Cookies;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Auth;
using Powersoft.Reporting.Data.Central;
using Powersoft.Reporting.Data.Factories;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.Options;
using Powersoft.Reporting.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Email
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender, BrevoSmtpEmailSender>();

// Central DB — decrypt password if stored encrypted (production pattern)
var centralConnString = builder.Configuration.GetConnectionString("PSCentral");
if (!string.IsNullOrEmpty(centralConnString))
{
    centralConnString = Cryptography.DecryptPasswordInConnectionString(centralConnString);
    builder.Services.AddSingleton<ICentralRepository>(sp => new CentralRepository(centralConnString));
    builder.Services.AddSingleton<IAuthenticationService>(sp => new AuthenticationService(centralConnString));
}

builder.Services.AddSingleton<ITenantRepositoryFactory, TenantRepositoryFactory>();
builder.Services.AddTransient<ScheduleExecutionService>();

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

// Scheduler runner — called by external cron/timer or manually for testing.
// In production, protect with API key or internal-only network.
app.MapPost("/api/run-scheduled-reports", async (ScheduleExecutionService runner, CancellationToken ct) =>
{
    var summary = await runner.RunAllDueSchedulesAsync(ct);
    return Results.Ok(summary);
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/test-email", async (IEmailSender emailSender, string to) =>
    {
        try
        {
            await emailSender.SendAsync(
                to,
                "Powersoft Reporting — Test Email",
                "<h2>It works!</h2><p>This is a test email from the Reporting Engine.</p>",
                "It works! This is a test email from the Reporting Engine.");
            return Results.Ok(new { success = true, message = $"Test email sent to {to}" });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    });
}

app.Run();
