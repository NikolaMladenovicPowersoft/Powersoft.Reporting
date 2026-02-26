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
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ITenantRepositoryFactory repositoryFactory,
        ILogger<SettingsController> logger)
    {
        _repositoryFactory = repositoryFactory;
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
