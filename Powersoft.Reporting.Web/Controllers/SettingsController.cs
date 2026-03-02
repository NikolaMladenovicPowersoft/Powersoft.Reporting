using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly ICentralRepository _centralRepository;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ITenantRepositoryFactory repositoryFactory,
        ICentralRepository centralRepository,
        ILogger<SettingsController> logger)
    {
        _repositoryFactory = repositoryFactory;
        _centralRepository = centralRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Database()
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
        {
            _logger.LogWarning("User {User} (ranking {Ranking}) denied access to DB settings",
                User.Identity?.Name, ranking);
            return RedirectToAction("AccessDenied", "Account");
        }

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        var repo = _repositoryFactory.CreateIniRepository(connString);
        var ini = await repo.GetLayoutAsync(
            ModuleConstants.ModuleCode,
            ModuleConstants.IniHeaderDbSettings,
            "ALL");

        var settings = DatabaseSettings.FromDictionary(ini);

        var vm = new DatabaseSettingsViewModel
        {
            ConnectedDatabase = GetConnectedDatabaseName(),
            ConnectedDatabaseCode = HttpContext.Session.GetString(SessionKeys.ConnectedDatabaseCode),
            MaxSchedulesPerReport = settings.MaxSchedulesPerReport,
            DefaultExportFormat = settings.DefaultExportFormat,
            SchedulerEnabled = settings.SchedulerEnabled,
            RetentionDays = settings.RetentionDays,
            IsSystemAdmin = ranking < ModuleConstants.RankingSystemAdmin,
            CanEdit = ranking <= ModuleConstants.RankingSystemAdmin
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Database(DatabaseSettingsViewModel vm)
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
            return RedirectToAction("AccessDenied", "Account");

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
            return RedirectToAction("Index", "Home");

        if (!ModelState.IsValid)
        {
            vm.ConnectedDatabase = GetConnectedDatabaseName();
            vm.CanEdit = true;
            return View(vm);
        }

        var settings = new DatabaseSettings
        {
            MaxSchedulesPerReport = vm.MaxSchedulesPerReport,
            DefaultExportFormat = vm.DefaultExportFormat,
            SchedulerEnabled = vm.SchedulerEnabled,
            RetentionDays = vm.RetentionDays
        };

        var repo = _repositoryFactory.CreateIniRepository(connString);
        await repo.SaveLayoutAsync(
            ModuleConstants.ModuleCode,
            ModuleConstants.IniHeaderDbSettings,
            ModuleConstants.IniDescriptionDbSettings,
            "ALL",
            settings.ToDictionary());

        _logger.LogInformation("DB settings saved for {DB} by {User}",
            GetConnectedDatabaseName(), User.Identity?.Name);

        vm.ConnectedDatabase = GetConnectedDatabaseName();
        vm.CanEdit = true;
        vm.SuccessMessage = "Settings saved successfully.";
        return View(vm);
    }

    // ==================== System Settings (psCentral — Powersoft staff only) ====================

    [HttpGet]
    public async Task<IActionResult> System()
    {
        var ranking = GetRanking();
        if (ranking >= ModuleConstants.RankingSystemAdmin)
        {
            _logger.LogWarning("User {User} (ranking {Ranking}) denied access to system settings",
                User.Identity?.Name, ranking);
            return RedirectToAction("AccessDenied", "Account");
        }

        var dict = await _centralRepository.GetSystemSettingsAsync("RE_");
        var settings = SystemSettings.FromDictionary(dict);

        var vm = new SystemSettingsViewModel
        {
            SchedulerMasterEnabled = settings.SchedulerMasterEnabled,
            MaxDatabasesPerRun = settings.MaxDatabasesPerRun,
            GlobalMaxSchedulesPerReport = settings.GlobalMaxSchedulesPerReport,
            DefaultRetentionDays = settings.DefaultRetentionDays,
            DefaultSmtpFromEmail = settings.DefaultSmtpFromEmail,
            DefaultSmtpFromName = settings.DefaultSmtpFromName
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> System(SystemSettingsViewModel vm)
    {
        var ranking = GetRanking();
        if (ranking >= ModuleConstants.RankingSystemAdmin)
            return RedirectToAction("AccessDenied", "Account");

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            _logger.LogWarning("System settings ModelState invalid: {Errors}", string.Join("; ", errors));
            vm.ErrorMessage = "Validation failed: " + string.Join(", ", errors);
            return View(vm);
        }

        try
        {
            var settings = new SystemSettings
            {
                SchedulerMasterEnabled = vm.SchedulerMasterEnabled,
                MaxDatabasesPerRun = vm.MaxDatabasesPerRun,
                GlobalMaxSchedulesPerReport = vm.GlobalMaxSchedulesPerReport,
                DefaultRetentionDays = vm.DefaultRetentionDays,
                DefaultSmtpFromEmail = vm.DefaultSmtpFromEmail ?? "",
                DefaultSmtpFromName = vm.DefaultSmtpFromName ?? ""
            };

            foreach (var (code, (desc, dataType, value)) in settings.ToSettingsList())
            {
                await _centralRepository.UpsertSystemSettingAsync(code, desc, dataType, value);
            }

            _logger.LogInformation("System settings saved by {User}", User.Identity?.Name);
            vm.SuccessMessage = "System settings saved successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save system settings");
            vm.ErrorMessage = $"Failed to save: {ex.Message}";
        }

        return View(vm);
    }

    // ==================== Email Templates ====================

    [HttpGet]
    public async Task<IActionResult> EmailTemplates()
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
            return RedirectToAction("AccessDenied", "Account");

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(connString);
            var templates = await repo.GetEmailTemplatesAsync();
            ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
            return View(templates);
        }
        catch
        {
            ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
            return View(new List<Core.Models.EmailTemplate>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditEmailTemplate(int? id)
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
            return RedirectToAction("AccessDenied", "Account");

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
            return RedirectToAction("Index", "Home");

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        if (id.HasValue)
        {
            var repo = _repositoryFactory.CreateScheduleRepository(connString);
            var template = await repo.GetEmailTemplateByIdAsync(id.Value);
            if (template == null) return NotFound();
            return View(template);
        }

        return View(new Core.Models.EmailTemplate
        {
            TemplateName = "",
            EmailSubject = "Report: «ReportName» — «Period»",
            EmailBodyHtml = GetDefaultTemplateHtml(),
            IsDefault = false
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEmailTemplate(Core.Models.EmailTemplate model)
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
            return RedirectToAction("AccessDenied", "Account");

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
            return RedirectToAction("Index", "Home");

        if (string.IsNullOrWhiteSpace(model.TemplateName))
        {
            TempData["Error"] = "Template name is required.";
            ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
            return View("EditEmailTemplate", model);
        }

        model.CreatedBy = User.Identity?.Name ?? "Unknown";

        var repo = _repositoryFactory.CreateScheduleRepository(connString);

        if (model.TemplateId > 0)
        {
            await repo.UpdateEmailTemplateAsync(model);
            TempData["Success"] = "Template updated successfully.";
        }
        else
        {
            await repo.CreateEmailTemplateAsync(model);
            TempData["Success"] = "Template created successfully.";
        }

        return RedirectToAction("EmailTemplates");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteEmailTemplate(int id)
    {
        var ranking = GetRanking();
        if (ranking > ModuleConstants.RankingSystemAdmin)
            return Json(new { success = false, message = "Access denied" });

        var connString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connString))
            return Json(new { success = false, message = "Not connected" });

        var repo = _repositoryFactory.CreateScheduleRepository(connString);
        await repo.DeleteEmailTemplateAsync(id);
        return Json(new { success = true });
    }

    private static string GetDefaultTemplateHtml()
    {
        return @"<div style=""font-family:Arial,sans-serif;max-width:600px;"">
<h2 style=""color:#2563eb;"">«ReportName»</h2>
<p>Please find attached the <strong>«ReportName»</strong> report for <strong>«DatabaseName»</strong>.</p>
<table style=""border-collapse:collapse;width:100%;margin:16px 0;"">
<tr><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;"">Period</td><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;""><strong>«Period»</strong></td></tr>
<tr><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;"">Rows</td><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;"">«RowCount»</td></tr>
<tr><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;"">Format</td><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;"">«ExportFormat»</td></tr>
<tr><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;"">Generated</td><td style=""padding:6px 12px;border-bottom:1px solid #e5e7eb;"">«GeneratedDate»</td></tr>
</table>
<p style=""color:#9ca3af;font-size:11px;margin-top:24px;"">This is an automated report from Powersoft Reporting Engine.</p>
</div>";
    }

    private string? GetTenantConnectionString() =>
        HttpContext.Session.GetString(SessionKeys.TenantConnectionString);

    private string? GetConnectedDatabaseName() =>
        HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);

    private int GetRanking()
    {
        var fromSession = HttpContext.Session.GetInt32(SessionKeys.Ranking);
        if (fromSession.HasValue) return fromSession.Value;

        var claim = User.FindFirst(AppClaimTypes.Ranking)?.Value;
        if (int.TryParse(claim, out var ranking))
        {
            HttpContext.Session.SetInt32(SessionKeys.Ranking, ranking);
            return ranking;
        }
        return 99;
    }
}
