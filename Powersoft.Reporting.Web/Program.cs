using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Auth;
using Powersoft.Reporting.Data.Central;
using Powersoft.Reporting.Data.Factories;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.Options;
using Powersoft.Reporting.Web.Services;
using Powersoft.Reporting.Web.Services.AI;
using Powersoft.Reporting.Web.Services.Storage;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/reporting-startup-.txt", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

Serilog.Debugging.SelfLog.Enable(msg => Console.Error.WriteLine($"[Serilog SelfLog] {msg}"));

builder.Services.Configure<HostOptions>(opts =>
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

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
builder.Services.AddScoped<ScheduleExecutionService>();
builder.Services.AddHostedService<ScheduleBackgroundService>();
builder.Services.AddHostedService<RetentionCleanupService>();

// DigitalOcean Spaces (S3-compatible cold storage)
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IReportStorageService, S3ReportStorageService>();

// AI Report Analyzer
builder.Services.Configure<AiAnalyzerOptions>(builder.Configuration.GetSection("AiAnalyzer"));
builder.Services.AddHttpClient("ClaudeAI");
builder.Services.AddHttpClient("OpenAI");
builder.Services.AddTransient<ClaudeReportAnalyzer>();
builder.Services.AddTransient<OpenAIReportAnalyzer>();
builder.Services.AddTransient<ReportAnalyzerFactory>();
builder.Services.AddTransient<DataChatService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Persist DataProtection keys to disk so auth cookies survive app pool recycles / restarts.
// Without this, every restart regenerates the ephemeral key ring, silently invalidating every
// issued auth cookie — so the next POST from a long-lived browser tab (e.g. clicking a saved
// layout) gets redirected to /Account/Login instead of running.
var dpKeysPath = Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
Directory.CreateDirectory(dpKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
    .SetApplicationName("PowersoftReporting");

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

var migLogger = app.Services.GetRequiredService<ILogger<Powersoft.Reporting.Data.Tenant.SchemaMigrationService>>();
Powersoft.Reporting.Data.Tenant.SchemaMigrationService.LogInfo = msg => migLogger.LogInformation(msg);
Powersoft.Reporting.Data.Tenant.SchemaMigrationService.LogWarning = (msg, ex) => migLogger.LogWarning(ex, msg);

// Startup health check: warn loudly if e-mail delivery is not configured.
// Without this, scheduled reports run successfully but recipients never get the mail.
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var emailOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value;
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(emailOpts.SmtpHost)) missing.Add("SmtpHost");
    if (string.IsNullOrWhiteSpace(emailOpts.SmtpUser) || emailOpts.SmtpUser.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) missing.Add("SmtpUser");
    if (string.IsNullOrWhiteSpace(emailOpts.SmtpPassword) || emailOpts.SmtpPassword.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) missing.Add("SmtpPassword");
    if (string.IsNullOrWhiteSpace(emailOpts.FromEmail)) missing.Add("FromEmail");
    if (missing.Count > 0)
    {
        startupLogger.LogError(
            "Email configuration is INCOMPLETE (missing/placeholder: {Missing}). " +
            "Scheduled reports will NOT deliver e-mails. " +
            "Set the 'Email' section in appsettings.json (SmtpHost, SmtpUser, SmtpPassword, FromEmail).",
            string.Join(", ", missing));
    }
    else
    {
        startupLogger.LogInformation(
            "Email configured: host={Host}:{Port}, from={From}",
            emailOpts.SmtpHost, emailOpts.SmtpPort, emailOpts.FromEmail);
    }
}

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

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
