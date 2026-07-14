using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Controllers;

/// <summary>
/// Admin UI for dboReportsAI.tbl_CashFlowMapping — the per-tenant mapping of COA account-code
/// ranges to Cash Flow statement sections (George, Splash training call 2026-07-09: administrators
/// must be able to manage account ranges directly, without code changes).
///
/// Access mirrors Database/System Settings: Ranking &lt;= 15 (system admin). The mapping lives in
/// the CONNECTED tenant database, so a company connection is required. Every change affects the
/// Cash Flow report of that tenant only.
/// </summary>
[Authorize]
public class CashFlowMappingController : Controller
{
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly ILogger<CashFlowMappingController> _logger;

    public CashFlowMappingController(
        ITenantRepositoryFactory repositoryFactory,
        ILogger<CashFlowMappingController> logger)
    {
        _repositoryFactory = repositoryFactory;
        _logger = logger;
    }

    // ── access helpers (same pattern as SettingsController) ──

    private string? GetTenantConnectionString() =>
        HttpContext.Session.GetString(SessionKeys.TenantConnectionString);

    private string? GetConnectedDatabaseName() =>
        HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);

    private string GetUserCode() =>
        HttpContext.Session.GetString(SessionKeys.UserCode) ?? User.Identity?.Name ?? "UNKNOWN";

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

    /// <summary>Single authority for "who may edit the cash flow mapping" — system admins, like Database Settings.</summary>
    private bool CanManageMapping() => GetRanking() <= ModuleConstants.RankingSystemAdmin;

    // ── pages ──

    [HttpGet]
    public IActionResult Index()
    {
        if (!CanManageMapping())
        {
            _logger.LogWarning("User {User} (ranking {Ranking}) denied cash flow mapping admin",
                User.Identity?.Name, GetRanking());
            return RedirectToAction("AccessDenied", "Account");
        }

        if (string.IsNullOrEmpty(GetTenantConnectionString()))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
        return View();
    }

    // ── JSON API ──

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!CanManageMapping()) return Json(new { success = false, message = "Not authorized." });
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected to a database." });

        try
        {
            var repo = _repositoryFactory.CreateCashFlowMappingRepository(conn);
            var rows = await repo.GetAllAsync();
            return Json(new
            {
                success = true,
                rows = rows.Select(r => new
                {
                    id = r.PkMappingID,
                    groupName = r.GroupName,
                    groupSortOrder = r.GroupSortOrder,
                    categoryName = r.CategoryName,
                    categorySortOrder = r.CategorySortOrder,
                    codeFrom = r.CodeFrom,
                    codeTo = r.CodeTo
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cash flow mapping");
            return Json(new { success = false, message = "Could not load the mapping table." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        int id, string groupName, int groupSortOrder,
        string categoryName, int categorySortOrder, string codeFrom, string codeTo)
    {
        if (!CanManageMapping()) return Json(new { success = false, message = "Not authorized." });
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected to a database." });

        var entry = new CashFlowMappingEntry
        {
            PkMappingID = id,
            GroupName = groupName ?? "",
            GroupSortOrder = groupSortOrder,
            CategoryName = categoryName ?? "",
            CategorySortOrder = categorySortOrder,
            CodeFrom = codeFrom ?? "",
            CodeTo = codeTo ?? ""
        };

        var error = entry.Validate();
        if (error != null) return Json(new { success = false, message = error });

        try
        {
            var repo = _repositoryFactory.CreateCashFlowMappingRepository(conn);
            if (entry.PkMappingID > 0)
            {
                if (!await repo.UpdateAsync(entry))
                    return Json(new { success = false, message = "Row not found — it may have been deleted." });
                _logger.LogInformation("CashFlowMapping {Id} updated by {User} on {Db}: {Group}/{Category} {From}-{To}",
                    entry.PkMappingID, GetUserCode(), GetConnectedDatabaseName(),
                    entry.GroupName, entry.CategoryName, entry.CodeFrom, entry.CodeTo);
                return Json(new { success = true, id = entry.PkMappingID, message = "Range updated." });
            }

            var newId = await repo.InsertAsync(entry);
            _logger.LogInformation("CashFlowMapping {Id} created by {User} on {Db}: {Group}/{Category} {From}-{To}",
                newId, GetUserCode(), GetConnectedDatabaseName(),
                entry.GroupName, entry.CategoryName, entry.CodeFrom, entry.CodeTo);
            return Json(new { success = true, id = newId, message = "Range added." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cash flow mapping row {Id}", id);
            return Json(new { success = false, message = "Failed to save the range." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!CanManageMapping()) return Json(new { success = false, message = "Not authorized." });
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected to a database." });
        if (id <= 0) return Json(new { success = false, message = "Row id required." });

        try
        {
            var repo = _repositoryFactory.CreateCashFlowMappingRepository(conn);
            if (!await repo.DeleteAsync(id))
                return Json(new { success = false, message = "Row not found — it may already be deleted." });

            _logger.LogInformation("CashFlowMapping {Id} deleted by {User} on {Db}", id, GetUserCode(), GetConnectedDatabaseName());
            return Json(new { success = true, message = "Range deleted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete cash flow mapping row {Id}", id);
            return Json(new { success = false, message = "Failed to delete the range." });
        }
    }

    /// <summary>Test tool: shows where a given account code lands, using the report's exact resolution rule.</summary>
    [HttpGet]
    public async Task<IActionResult> Resolve(string accountCode)
    {
        if (!CanManageMapping()) return Json(new { success = false, message = "Not authorized." });
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected to a database." });
        if (string.IsNullOrWhiteSpace(accountCode))
            return Json(new { success = false, message = "Enter an account code to test." });

        try
        {
            var repo = _repositoryFactory.CreateCashFlowMappingRepository(conn);
            var r = await repo.ResolveAccountAsync(accountCode);
            return Json(new
            {
                success = true,
                matched = r.Matched,
                groupName = r.GroupName,
                categoryName = r.CategoryName,
                id = r.PkMappingID,
                codeFrom = r.CodeFrom,
                codeTo = r.CodeTo,
                matchCount = r.MatchCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve account {Code}", accountCode);
            return Json(new { success = false, message = "Failed to test the account code." });
        }
    }

    /// <summary>Replaces the whole mapping with the default (ARVA/PBIX) 48-row seed.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetToDefaults()
    {
        if (!CanManageMapping()) return Json(new { success = false, message = "Not authorized." });
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected to a database." });

        try
        {
            var repo = _repositoryFactory.CreateCashFlowMappingRepository(conn);
            var count = await repo.ResetToDefaultsAsync();
            _logger.LogWarning("CashFlowMapping RESET to defaults by {User} on {Db} ({Count} rows)",
                GetUserCode(), GetConnectedDatabaseName(), count);
            return Json(new { success = true, count, message = $"Mapping reset to the default {count} ranges." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset cash flow mapping");
            return Json(new { success = false, message = "Failed to reset the mapping." });
        }
    }
}
