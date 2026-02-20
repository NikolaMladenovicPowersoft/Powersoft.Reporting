using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ICentralRepository _centralRepository;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ICentralRepository centralRepository, ILogger<HomeController> logger)
    {
        _centralRepository = centralRepository;
        _logger = logger;
    }

    private string GetUserCode() =>
        HttpContext.Session.GetString(SessionKeys.UserCode)
        ?? User.FindFirst(AppClaimTypes.UserCode)?.Value
        ?? User.Identity?.Name ?? "";

    private int GetRanking()
    {
        var fromSession = HttpContext.Session.GetInt32(SessionKeys.Ranking);
        if (fromSession.HasValue) return fromSession.Value;

        // Fallback to claims (session expired but cookie still valid)
        var claim = User.FindFirst(AppClaimTypes.Ranking)?.Value;
        if (int.TryParse(claim, out var ranking))
        {
            HttpContext.Session.SetInt32(SessionKeys.Ranking, ranking);
            return ranking;
        }
        return 99;
    }

    private int GetRoleID()
    {
        var fromSession = HttpContext.Session.GetInt32(SessionKeys.RoleID);
        if (fromSession.HasValue) return fromSession.Value;

        var claim = User.FindFirst(AppClaimTypes.RoleID)?.Value;
        if (int.TryParse(claim, out var roleId))
        {
            HttpContext.Session.SetInt32(SessionKeys.RoleID, roleId);
            return roleId;
        }
        return 0;
    }

    public async Task<IActionResult> Index()
    {
        var userCode = GetUserCode();
        var ranking = GetRanking();

        var viewModel = new DatabaseSelectionViewModel
        {
            UserCode = userCode,
            DisplayName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value,
            RoleName = User.FindFirst(AppClaimTypes.RoleName)?.Value,
            Ranking = ranking
        };

        try
        {
            // Check if already connected
            var connectedDb = HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);
            if (!string.IsNullOrEmpty(connectedDb))
            {
                viewModel.ConnectedDatabaseName = connectedDb;
                viewModel.IsConnected = true;
            }

            // Get accessible databases (filtered by ranking + module + user-DB link)
            var accessibleDbs = await _centralRepository.GetAccessibleDatabasesAsync(userCode, ranking);
            viewModel.AccessibleDatabases = accessibleDbs;

            // Extract distinct companies from the accessible databases
            viewModel.Companies = accessibleDbs
                .GroupBy(d => d.CompanyCode)
                .Select(g => new Core.Models.Company
                {
                    CompanyCode = g.Key,
                    CompanyName = g.First().CompanyName ?? g.Key,
                    CompanyActive = true
                })
                .OrderBy(c => c.CompanyName)
                .ToList();

            // Auto-login: if exactly 1 company + 1 database and not already connected → connect automatically
            if (!viewModel.IsConnected && accessibleDbs.Count == 1)
            {
                var singleDb = accessibleDbs[0];
                var autoConnectResult = await TryConnectToDatabase(singleDb.DBCode);
                if (autoConnectResult.success)
                {
                    _logger.LogInformation(
                        "Auto-login: user {User} → single accessible DB {DB}",
                        userCode, singleDb.DBFriendlyName);
                    return RedirectToAction("Index", "Reports");
                }
            }

            // If no accessible databases, show a clear message
            if (accessibleDbs.Count == 0)
            {
                viewModel.ErrorMessage = ranking < ModuleConstants.RankingSystemAdmin
                    ? "No active databases found."
                    : "You don't have access to any databases with the Reporting module. Please contact your administrator.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accessible databases for user {User}", userCode);
            viewModel.ErrorMessage = "Unable to connect to PS Central database. Please check configuration.";
        }
        
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> GetDatabases(string companyCode)
    {
        try
        {
            var userCode = GetUserCode();
            var ranking = GetRanking();

            // Get all accessible databases, then filter by company
            var accessibleDbs = await _centralRepository.GetAccessibleDatabasesAsync(userCode, ranking);
            var filtered = accessibleDbs
                .Where(d => d.CompanyCode == companyCode)
                .Select(d => new { value = d.DBCode, text = d.DBFriendlyName });

            return Json(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading databases for company {CompanyCode}", companyCode);
            return Json(new { error = "Failed to load databases" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Connect(string databaseCode)
    {
        var userCode = GetUserCode();
        var ranking = GetRanking();

        try
        {
            // Security check: verify user actually has access to this database
            var canAccess = await _centralRepository.CanUserAccessDatabaseAsync(userCode, ranking, databaseCode);
            if (!canAccess)
            {
                _logger.LogWarning(
                    "User {User} (Ranking {Ranking}) attempted to access unauthorized database {DB}",
                    userCode, ranking, databaseCode);
                return Json(new { success = false, message = "You don't have access to this database." });
            }

            var result = await TryConnectToDatabase(databaseCode);
            return Json(result.success
                ? new { success = true, databaseName = result.dbName }
                : new { success = false, message = result.error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to database {DatabaseCode}", databaseCode);
            return Json(new { success = false, message = "Connection failed. Please try again or contact support." });
        }
    }

    [HttpPost]
    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove(SessionKeys.TenantConnectionString);
        HttpContext.Session.Remove(SessionKeys.ConnectedDatabase);
        HttpContext.Session.Remove(SessionKeys.ConnectedDatabaseCode);
        
        return RedirectToAction("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Internal helper to connect to a database and store the connection in session.
    /// Returns (success, dbName, error).
    /// </summary>
    private async Task<(bool success, string? dbName, string? error)> TryConnectToDatabase(string databaseCode)
    {
        var database = await _centralRepository.GetDatabaseByCodeAsync(databaseCode);
        if (database == null)
        {
            return (false, null, "Database not found.");
        }

        var tenantConnString = ConnectionStringBuilder.Build(database);

        // Verify the tenant database is reachable
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
        await conn.OpenAsync();

        HttpContext.Session.SetString(SessionKeys.TenantConnectionString, tenantConnString);
        HttpContext.Session.SetString(SessionKeys.ConnectedDatabase, database.DBFriendlyName);
        HttpContext.Session.SetString(SessionKeys.ConnectedDatabaseCode, database.DBCode);

        _logger.LogInformation("Connected to database {DB} ({DBCode})", database.DBFriendlyName, database.DBCode);

        return (true, database.DBFriendlyName, null);
    }
}
