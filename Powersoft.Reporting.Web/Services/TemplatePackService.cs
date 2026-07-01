using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services;

/// <summary>
/// Applies industry template packs to a company: clones each pack item into the tenant's
/// dboReportsAI.tbl_ReportSchedule and records the applied pack in the APPLIEDPACKS INI header
/// (per-tenant) so re-applying is idempotent.
/// </summary>
public class TemplatePackService
{
    private readonly ITenantRepositoryFactory _factory;
    private readonly ITemplatePackCatalog _catalog;
    private readonly ILogger<TemplatePackService> _logger;

    public TemplatePackService(
        ITenantRepositoryFactory factory,
        ITemplatePackCatalog catalog,
        ILogger<TemplatePackService> logger)
    {
        _factory = factory;
        _catalog = catalog;
        _logger = logger;
    }

    public Task<IReadOnlyList<ReportTemplatePack>> GetPacksAsync() => _catalog.GetPacksAsync();

    /// <summary>
    /// Raw applied-marker keys from the APPLIEDPACKS INI header. Keys are either a bare pack code
    /// ("FASHION" — legacy, whole pack) or per-item "{PackCode}:{ItemKey}" (current). Use
    /// <see cref="IsItemApplied"/> to interpret them for a specific item.
    /// </summary>
    public async Task<HashSet<string>> GetAppliedKeysAsync(string connectionString)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var iniRepo = _factory.CreateIniRepository(connectionString);
            var ini = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode, ModuleConstants.IniHeaderAppliedPacks, "ALL");
            foreach (var key in ini.Keys)
                applied.Add(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read applied template packs; assuming none applied");
        }
        return applied;
    }

    /// <summary>Per-item applied check: true if the item's key OR a legacy whole-pack marker is present.</summary>
    public static bool IsItemApplied(HashSet<string> appliedKeys, string packCode, string itemKey) =>
        appliedKeys.Contains(packCode) || appliedKeys.Contains(ItemMarker(packCode, itemKey));

    private static string ItemMarker(string packCode, string itemKey) => $"{packCode}:{itemKey}";

    /// <summary>
    /// Clones the selected reports of a pack into the tenant schedule table with the given recipients.
    /// Idempotent per item: items already applied to this company are skipped (never duplicated), so an
    /// admin can apply a subset now and add more later. If <paramref name="selectedItemKeys"/> is null or
    /// empty, all items in the pack are applied (backward-compatible whole-pack apply).
    /// </summary>
    public async Task<TemplatePackApplyResult> ApplyPackAsync(
        string connectionString, string packCode, string recipients, string userCode,
        IReadOnlyCollection<string>? selectedItemKeys = null)
    {
        var pack = await _catalog.GetPackAsync(packCode);
        if (pack == null)
            return new TemplatePackApplyResult { Success = false, Message = "Template pack not found." };

        if (string.IsNullOrWhiteSpace(recipients))
            return new TemplatePackApplyResult { Success = false, PackName = pack.PackName, Message = "Recipients are required." };

        // Resolve which items to apply. Empty selection => all items.
        var wantAll = selectedItemKeys == null || selectedItemKeys.Count == 0;
        var wanted = wantAll
            ? null
            : new HashSet<string>(selectedItemKeys!, StringComparer.OrdinalIgnoreCase);

        var targetItems = pack.Items
            .Where(i => wantAll || wanted!.Contains(i.ItemKey))
            .ToList();

        if (targetItems.Count == 0)
            return new TemplatePackApplyResult { Success = false, PackName = pack.PackName, Message = "No matching reports selected." };

        var iniRepo = _factory.CreateIniRepository(connectionString);
        var existing = await iniRepo.GetLayoutAsync(
            ModuleConstants.ModuleCode, ModuleConstants.IniHeaderAppliedPacks, "ALL");
        var appliedKeys = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);

        var scheduleRepo = _factory.CreateScheduleRepository(connectionString);
        var created = 0;
        var skipped = 0;
        var newMarkers = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var item in targetItems)
        {
            if (IsItemApplied(appliedKeys, pack.PackCode, item.ItemKey))
            {
                skipped++;
                continue;
            }

            var schedule = new ReportSchedule
            {
                ReportType = item.ReportType,
                ScheduleName = item.TemplateName,
                CreatedBy = userCode,
                RecurrenceType = item.RecurrenceType,
                RecurrenceDay = item.RecurrenceDay,
                ScheduleTime = item.ScheduleTime,
                ExportFormat = item.ExportFormat,
                Recipients = recipients,
                EmailSubject = item.TemplateName,
                // Structural, portable params only. Permission flags default to safe (ViewCost/ViewSupplier
                // = true via ScheduleParameters defaults); refine to the applying admin's perms later.
                ParametersJson = item.ParametersJson,
                RecurrenceJson = null,
                NextRunDate = ComputeNextRun(item.RecurrenceType, item.RecurrenceDay, item.ScheduleTime),
                IncludeAiAnalysis = item.IncludeAiAnalysis,
                AiLocale = item.AiLocale,
                SkipIfEmpty = item.SkipIfEmpty
            };

            await scheduleRepo.CreateScheduleAsync(schedule);
            newMarkers[ItemMarker(pack.PackCode, item.ItemKey)] = DateTime.UtcNow.ToString("o");
            created++;
        }

        if (created == 0)
        {
            return new TemplatePackApplyResult
            {
                Success = true,
                AlreadyApplied = true,
                SkippedCount = skipped,
                PackName = pack.PackName,
                Message = $"All selected report(s) from '{pack.PackName}' were already applied to this company."
            };
        }

        // Merge new item markers into the existing header so prior packs/items are preserved.
        await iniRepo.SaveLayoutAsync(
            ModuleConstants.ModuleCode, ModuleConstants.IniHeaderAppliedPacks,
            ModuleConstants.IniDescriptionAppliedPacks, "ALL", newMarkers);

        _logger.LogInformation("Applied template pack {Pack}: {Created} created, {Skipped} skipped, by {User}",
            pack.PackCode, created, skipped, userCode);

        var msg = skipped > 0
            ? $"{created} scheduled report(s) created from '{pack.PackName}'; {skipped} already existed."
            : $"{created} scheduled report(s) created from '{pack.PackName}'.";

        return new TemplatePackApplyResult
        {
            Success = true,
            CreatedCount = created,
            SkippedCount = skipped,
            PackName = pack.PackName,
            Message = msg
        };
    }

    /// <summary>
    /// Next run date from a simple recurrence (mirrors ReportsController.CalculateNextRun).
    /// Packs use Monthly-on-day recurrence; Daily/Weekly are supported for completeness.
    /// </summary>
    private static DateTime ComputeNextRun(string recurrenceType, int? day, TimeSpan time)
    {
        var now = DateTime.Now;
        switch ((recurrenceType ?? "Daily").ToLowerInvariant())
        {
            case "monthly":
            {
                var d = day is >= 1 and <= 31 ? day.Value : 1;
                var maxDay = DateTime.DaysInMonth(now.Year, now.Month);
                var candidate = new DateTime(now.Year, now.Month, Math.Min(d, maxDay)).Add(time);
                if (candidate > now) return candidate;
                var ny = now.Month == 12 ? now.Year + 1 : now.Year;
                var nm = now.Month == 12 ? 1 : now.Month + 1;
                return new DateTime(ny, nm, Math.Min(d, DateTime.DaysInMonth(ny, nm))).Add(time);
            }
            case "weekly":
            {
                var target = (DayOfWeek)((day ?? 1) % 7);
                var daysUntil = ((int)target - (int)now.DayOfWeek + 7) % 7;
                var wd = now.Date.AddDays(daysUntil).Add(time);
                return wd > now ? wd : wd.AddDays(7);
            }
            default: // daily
            {
                var t = now.Date.Add(time);
                return t > now ? t : t.AddDays(1);
            }
        }
    }
}
