using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Data.Tenant;
using Powersoft.Reporting.Web.Services;

namespace Powersoft.Reporting.Web.Controllers;

/// <summary>
/// Webmaster-only authoring of the central industry template catalog (create/edit/delete packs and
/// their reports). Mirrors the access gate used for the cross-tenant AI usage report: only the
/// top-level Powersoft webmaster (Ranking == 1) may curate packs shared across all companies.
/// The apply/consume side (<see cref="TemplatePacksController"/>) stays open to any authenticated user.
/// </summary>
[Authorize]
public class TemplateAdminController : Controller
{
    private readonly ICentralRepository _central;
    private readonly ITenantRepositoryFactory _factory;
    private readonly TemplatePackService _packService;
    private readonly ILogger<TemplateAdminController> _logger;
    private readonly string? _psCentralConnString;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly Regex PackCodeRegex = new("^[A-Z0-9_]{2,50}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TemplateAdminController(
        ICentralRepository central, ITenantRepositoryFactory factory, TemplatePackService packService,
        IConfiguration configuration, ILogger<TemplateAdminController> logger)
    {
        _central = central;
        _factory = factory;
        _packService = packService;
        _logger = logger;
        var raw = configuration.GetConnectionString("PSCentral");
        _psCentralConnString = !string.IsNullOrEmpty(raw) ? Cryptography.DecryptPasswordInConnectionString(raw) : null;
    }

    private string? GetTenantConnectionString() =>
        HttpContext.Session.GetString(SessionKeys.TenantConnectionString);

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

    /// <summary>
    /// Single authority for "who may author the shared template catalog".
    /// Policy (v1): only the top-level Powersoft webmaster (Ranking == 1) can create/edit packs,
    /// promote schedules into templates, and provision packs across companies — mirroring the
    /// cross-tenant AI-usage report gate. Applying an existing pack to one's OWN connected company
    /// stays open to any authenticated user (see <see cref="TemplatePacksController"/>).
    ///
    /// To widen authoring to system admins as well, change ONLY this line to
    /// <c>GetRanking() &lt;= ModuleConstants.RankingSystemAdmin</c>; every endpoint and the UI gate go through here.
    /// </summary>
    private bool CanAuthorTemplates() => GetRanking() == ModuleConstants.RankingWebmaster;

    private string GetUserCode() =>
        HttpContext.Session.GetString(SessionKeys.UserCode) ?? User.Identity?.Name ?? "UNKNOWN";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!CanAuthorTemplates())
        {
            _logger.LogWarning("User {User} (ranking {Ranking}) denied template admin", User.Identity?.Name, GetRanking());
            return RedirectToAction("AccessDenied", "Account");
        }

        List<ReportTemplatePack> packs;
        try { packs = await _central.GetTemplatePacksAsync(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load template packs for admin");
            packs = new List<ReportTemplatePack>();
            TempData["TemplateAdminError"] = "Could not load packs — the central template tables may not exist yet.";
        }

        ViewBag.ReportTypes = ReportTypeConstants.Schedulable.OrderBy(x => x).ToList();
        return View(packs);
    }

    [HttpGet]
    public async Task<IActionResult> Get(string code)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        if (string.IsNullOrWhiteSpace(code)) return Json(new { success = false, message = "Pack code required." });

        var pack = await _central.GetTemplatePackAsync(code);
        if (pack == null) return Json(new { success = false, message = "Pack not found." });

        return Json(new
        {
            success = true,
            pack = new
            {
                pack.PackCode,
                pack.PackName,
                pack.IndustryTag,
                pack.Description,
                pack.SortOrder,
                items = pack.Items.Select(i => new
                {
                    i.ReportType,
                    i.TemplateName,
                    i.ParametersJson,
                    i.RecurrenceType,
                    i.RecurrenceDay,
                    scheduleTimeMin = (int)i.ScheduleTime.TotalMinutes,
                    i.ExportFormat,
                    i.IncludeAiAnalysis,
                    i.AiLocale,
                    i.SkipIfEmpty
                }).ToList()
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string payload)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        if (string.IsNullOrWhiteSpace(payload))
            return Json(new { success = false, message = "Nothing to save." });

        PackDto? dto;
        try { dto = JsonSerializer.Deserialize<PackDto>(payload, JsonOpts); }
        catch { return Json(new { success = false, message = "Invalid pack data." }); }
        if (dto == null) return Json(new { success = false, message = "Invalid pack data." });

        var code = (dto.PackCode ?? "").Trim().ToUpperInvariant();
        if (!PackCodeRegex.IsMatch(code))
            return Json(new { success = false, message = "Pack code must be 2-50 chars: A-Z, 0-9, underscore." });
        if (string.IsNullOrWhiteSpace(dto.PackName))
            return Json(new { success = false, message = "Pack name is required." });
        if (dto.Items == null || dto.Items.Count == 0)
            return Json(new { success = false, message = "Add at least one report to the pack." });

        var pack = new ReportTemplatePack
        {
            PackCode = code,
            PackName = dto.PackName.Trim(),
            IndustryTag = string.IsNullOrWhiteSpace(dto.IndustryTag) ? null : dto.IndustryTag.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            SortOrder = dto.SortOrder
        };

        foreach (var it in dto.Items)
        {
            if (!ReportTypeConstants.IsSchedulable(it.ReportType ?? ""))
                return Json(new { success = false, message = $"Unknown report type '{it.ReportType}'." });
            if (string.IsNullOrWhiteSpace(it.TemplateName))
                return Json(new { success = false, message = "Every report needs a name." });

            // Templates must stay portable: strip any tenant-specific selections the author may have pasted.
            var cleanParams = TemplateParametersSanitizer.Strip(it.ParametersJson);

            pack.Items.Add(new ReportTemplateItem
            {
                ReportType = it.ReportType!,
                TemplateName = it.TemplateName.Trim(),
                ParametersJson = cleanParams,
                RecurrenceType = string.IsNullOrWhiteSpace(it.RecurrenceType) ? "Monthly" : it.RecurrenceType,
                RecurrenceDay = it.RecurrenceDay,
                ScheduleTime = TimeSpan.FromMinutes(Math.Clamp(it.ScheduleTimeMin, 0, 1439)),
                ExportFormat = string.IsNullOrWhiteSpace(it.ExportFormat) ? "Excel" : it.ExportFormat,
                IncludeAiAnalysis = it.IncludeAiAnalysis,
                AiLocale = string.IsNullOrWhiteSpace(it.AiLocale) ? "en" : it.AiLocale,
                SkipIfEmpty = it.SkipIfEmpty
            });
        }

        try
        {
            await _central.UpsertTemplatePackAsync(pack, GetUserCode());
            _logger.LogInformation("Template pack {Pack} saved by {User} ({Count} items)",
                pack.PackCode, GetUserCode(), pack.Items.Count);
            return Json(new { success = true, message = $"Pack '{pack.PackName}' saved." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save template pack {Pack}", pack.PackCode);
            return Json(new { success = false, message = "Failed to save pack." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string packCode)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        if (string.IsNullOrWhiteSpace(packCode))
            return Json(new { success = false, message = "Pack code required." });

        try
        {
            await _central.DeleteTemplatePackAsync(packCode.Trim().ToUpperInvariant());
            _logger.LogInformation("Template pack {Pack} deleted by {User}", packCode, GetUserCode());
            return Json(new { success = true, message = "Pack deleted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template pack {Pack}", packCode);
            return Json(new { success = false, message = "Failed to delete pack." });
        }
    }

    /// <summary>Lightweight pack list (code + name + item count) for the "Save as Template" target picker.</summary>
    [HttpGet]
    public async Task<IActionResult> PackList()
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        try
        {
            var packs = await _central.GetTemplatePacksAsync();
            return Json(new
            {
                success = true,
                packs = packs.OrderBy(p => p.SortOrder).Select(p => new { p.PackCode, p.PackName, itemCount = p.Items.Count })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list packs");
            return Json(new { success = false, message = "Could not load packs." });
        }
    }

    /// <summary>
    /// Promotes an existing tenant schedule into a portable template item, either appending it to an
    /// existing pack or creating a new pack. Tenant-specific selections are stripped so the template
    /// stays portable. Webmaster-only, and requires a connected company (to read the source schedule).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScheduleAsTemplate(
        int scheduleId, string mode, string? packCode, string? packName,
        string? industryTag, string? description, string? itemName)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn))
            return Json(new { success = false, message = "Connect to a company first to read the schedule." });
        if (scheduleId <= 0)
            return Json(new { success = false, message = "A schedule must be selected." });

        ReportSchedule? schedule;
        try
        {
            var scheduleRepo = _factory.CreateScheduleRepository(conn);
            schedule = await scheduleRepo.GetScheduleByIdAsync(scheduleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read schedule {Id} for templating", scheduleId);
            return Json(new { success = false, message = "Could not read the source schedule." });
        }
        if (schedule == null)
            return Json(new { success = false, message = "Source schedule not found." });
        if (!ReportTypeConstants.IsSchedulable(schedule.ReportType ?? ""))
            return Json(new { success = false, message = $"Report type '{schedule.ReportType}' cannot be templated." });

        var name = string.IsNullOrWhiteSpace(itemName) ? (schedule.ScheduleName ?? "").Trim() : itemName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, message = "Give the template report a name." });

        var item = new ReportTemplateItem
        {
            ReportType = schedule.ReportType!,
            TemplateName = name,
            ParametersJson = TemplateParametersSanitizer.Strip(schedule.ParametersJson),
            RecurrenceType = string.IsNullOrWhiteSpace(schedule.RecurrenceType) ? "Monthly" : schedule.RecurrenceType,
            RecurrenceDay = schedule.RecurrenceDay,
            ScheduleTime = schedule.ScheduleTime,
            ExportFormat = string.IsNullOrWhiteSpace(schedule.ExportFormat) ? "Excel" : schedule.ExportFormat,
            IncludeAiAnalysis = schedule.IncludeAiAnalysis,
            AiLocale = string.IsNullOrWhiteSpace(schedule.AiLocale) ? "en" : schedule.AiLocale,
            SkipIfEmpty = schedule.SkipIfEmpty
        };

        ReportTemplatePack pack;
        bool createdNewPack = false;
        try
        {
            if (string.Equals(mode, "new", StringComparison.OrdinalIgnoreCase))
            {
                var code = (packCode ?? "").Trim().ToUpperInvariant();
                if (!PackCodeRegex.IsMatch(code))
                    return Json(new { success = false, message = "Pack code must be 2-50 chars: A-Z, 0-9, underscore." });
                if (string.IsNullOrWhiteSpace(packName))
                    return Json(new { success = false, message = "Pack name is required." });
                if (await _central.GetTemplatePackAsync(code) != null)
                    return Json(new { success = false, message = $"A pack with code '{code}' already exists. Pick it under \"Add to existing\"." });

                pack = new ReportTemplatePack
                {
                    PackCode = code,
                    PackName = packName.Trim(),
                    IndustryTag = string.IsNullOrWhiteSpace(industryTag) ? null : industryTag.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    SortOrder = 0
                };
                pack.Items.Add(item);
                createdNewPack = true;
            }
            else
            {
                var code = (packCode ?? "").Trim().ToUpperInvariant();
                var existing = await _central.GetTemplatePackAsync(code);
                if (existing == null)
                    return Json(new { success = false, message = "Select a target pack." });
                // Replace an item with the same key (edit-in-place) or append a new one.
                existing.Items.RemoveAll(i => string.Equals(i.ItemKey, item.ItemKey, StringComparison.OrdinalIgnoreCase));
                existing.Items.Add(item);
                pack = existing;
            }

            await _central.UpsertTemplatePackAsync(pack, GetUserCode());
            _logger.LogInformation(
                "Schedule {Id} saved as template into pack {Pack} by {User} (newPack={New})",
                scheduleId, pack.PackCode, GetUserCode(), createdNewPack);

            return Json(new
            {
                success = true,
                packCode = pack.PackCode,
                packName = pack.PackName,
                itemName = item.TemplateName,
                createdNewPack,
                message = createdNewPack
                    ? $"Created pack '{pack.PackName}' with '{item.TemplateName}'."
                    : $"Added '{item.TemplateName}' to pack '{pack.PackName}'."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save schedule {Id} as template", scheduleId);
            return Json(new { success = false, message = "Failed to save as template." });
        }
    }

    /// <summary>Companies (databases) the current admin can provision packs into, for the multi-company picker.</summary>
    [HttpGet]
    public async Task<IActionResult> ApplyTargets()
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        try
        {
            var dbs = await _central.GetAccessibleDatabasesAsync(GetUserCode(), GetRanking());
            return Json(new
            {
                success = true,
                databases = dbs
                    .OrderBy(d => d.CompanyName ?? d.CompanyCode).ThenBy(d => d.DBFriendlyName)
                    .Select(d => new { dbCode = d.DBCode, dbName = d.DBFriendlyName, company = d.CompanyName ?? d.CompanyCode })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load apply targets");
            return Json(new { success = false, message = "Could not load companies." });
        }
    }

    /// <summary>Items of a pack (for the item picker in the multi-company apply dialog).</summary>
    [HttpGet]
    public async Task<IActionResult> PackItems(string packCode)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        var pack = await _central.GetTemplatePackAsync((packCode ?? "").Trim());
        if (pack == null) return Json(new { success = false, message = "Pack not found." });
        return Json(new
        {
            success = true,
            packName = pack.PackName,
            items = pack.Items.Select(i => new { i.ItemKey, i.TemplateName, i.ReportType, i.RecurrenceType, i.IncludeAiAnalysis })
        });
    }

    /// <summary>
    /// Provisions a pack (optionally a subset of its items) into one or more companies at once. Each
    /// company gets the selected reports scheduled with the given recipients. Idempotent per company:
    /// items already applied there are skipped. Webmaster-only.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyToCompanies(
        string packCode, string recipients, string[]? dbCodes, string[]? selectedItems)
    {
        if (!CanAuthorTemplates()) return Json(new { success = false, message = "Not authorized." });
        if (string.IsNullOrWhiteSpace(packCode))
            return Json(new { success = false, message = "Pack is required." });

        var (validEmails, invalidEmails) = ParseAndValidateEmailList(recipients);
        if (validEmails.Length == 0)
            return Json(new { success = false, message = "At least one valid recipient email is required." });
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email(s): {string.Join(", ", invalidEmails)}" });

        var targets = (dbCodes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToArray();
        if (targets.Length == 0)
            return Json(new { success = false, message = "Select at least one company." });

        var selected = (selectedItems ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (selected.Length == 0)
            return Json(new { success = false, message = "Select at least one report to apply." });

        var userCode = GetUserCode();
        var ranking = GetRanking();
        var recipientsCsv = string.Join(",", validEmails);
        var results = new List<object>();
        int totalCreated = 0, companiesOk = 0;

        foreach (var dbCode in targets)
        {
            try
            {
                if (!await _central.CanUserAccessDatabaseAsync(userCode, ranking, dbCode))
                {
                    results.Add(new { dbCode, success = false, message = "No access." });
                    continue;
                }

                var database = await _central.GetDatabaseByCodeAsync(dbCode);
                if (database == null)
                {
                    results.Add(new { dbCode, success = false, message = "Database not found." });
                    continue;
                }

                var conn = !string.IsNullOrEmpty(_psCentralConnString)
                    ? ConnectionStringBuilder.BuildFromReference(database, _psCentralConnString)
                    : ConnectionStringBuilder.Build(database);

                await SchemaMigrationService.EnsureSchemaAsync(conn);

                var r = await _packService.ApplyPackAsync(conn, packCode, recipientsCsv, userCode, selected);
                totalCreated += r.CreatedCount;
                if (r.Success) companiesOk++;
                results.Add(new
                {
                    dbCode, company = database.DBFriendlyName, success = r.Success,
                    createdCount = r.CreatedCount, skippedCount = r.SkippedCount, message = r.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply pack {Pack} to {DB}", packCode, dbCode);
                results.Add(new { dbCode, success = false, message = "Apply failed (schema/connection)." });
            }
        }

        _logger.LogInformation(
            "Multi-apply pack {Pack} by {User}: {Ok}/{Total} companies, {Created} schedules created",
            packCode, userCode, companiesOk, targets.Length, totalCreated);

        return Json(new
        {
            success = companiesOk > 0,
            companiesOk,
            companiesTotal = targets.Length,
            totalCreated,
            results,
            message = $"Applied to {companiesOk}/{targets.Length} companies — {totalCreated} report(s) created."
        });
    }

    private static (string[] valid, string[] invalid) ParseAndValidateEmailList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (Array.Empty<string>(), Array.Empty<string>());
        var all = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (all.Where(e => EmailRegex.IsMatch(e)).ToArray(), all.Where(e => !EmailRegex.IsMatch(e)).ToArray());
    }

    // ── request DTOs (camelCase JSON from the editor) ──
    public class PackDto
    {
        public string? PackCode { get; set; }
        public string? PackName { get; set; }
        public string? IndustryTag { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    public class ItemDto
    {
        public string? ReportType { get; set; }
        public string? TemplateName { get; set; }
        public string? ParametersJson { get; set; }
        public string? RecurrenceType { get; set; }
        public int? RecurrenceDay { get; set; }
        public int ScheduleTimeMin { get; set; } = 480;
        public string? ExportFormat { get; set; }
        public bool IncludeAiAnalysis { get; set; }
        public string? AiLocale { get; set; }
        public bool SkipIfEmpty { get; set; } = true;
    }
}
