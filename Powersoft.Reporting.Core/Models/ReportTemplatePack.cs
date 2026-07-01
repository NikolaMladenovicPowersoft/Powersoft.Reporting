namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// An industry template pack: a curated, named set of report templates (e.g. "Fashion", "Supermarket")
/// that an admin can apply to a company to provision a ready-made set of scheduled reports.
///
/// POC: packs are code-seeded (see SeededTemplatePackCatalog). The full version will store these
/// centrally in psCentral (dbo.tbl_RE_TemplatePack + Item) so they can be authored without a redeploy.
/// A pack carries ONLY structural, portable configuration — never tenant-specific selections (category
/// IDs, store codes, item IDs), because those refer to different records in each company's database.
/// </summary>
public class ReportTemplatePack
{
    /// <summary>Stable unique code, e.g. "FASHION". Used as the idempotency key when applying.</summary>
    public string PackCode { get; set; } = string.Empty;

    /// <summary>Display name, e.g. "Fashion Retail".</summary>
    public string PackName { get; set; } = string.Empty;

    /// <summary>Optional industry label shown in the picker.</summary>
    public string? IndustryTag { get; set; }

    /// <summary>Short description of what the pack sets up.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public List<ReportTemplateItem> Items { get; set; } = new();
}

/// <summary>
/// One report inside a pack. Structurally a <see cref="ReportSchedule"/> minus the company-specific
/// parts (recipients + concrete selections). On apply it is cloned into the tenant's
/// dboReportsAI.tbl_ReportSchedule with recipients filled in.
/// </summary>
public class ReportTemplateItem
{
    private string? _itemKey;

    /// <summary>
    /// Stable key of this item WITHIN its pack. Used for per-item selection and per-item idempotency
    /// (APPLIEDPACKS stores "{PackCode}:{ItemKey}"). If not set explicitly (code-seeded packs), it is
    /// derived from <see cref="TemplateName"/> as a slug, which is unique within a pack. The DB-backed
    /// catalog will set this to the item's primary key.
    /// </summary>
    public string ItemKey
    {
        get => string.IsNullOrWhiteSpace(_itemKey) ? Slugify(TemplateName) : _itemKey!;
        set => _itemKey = value;
    }

    /// <summary>Report type constant, e.g. ReportTypeConstants.PurchasesSales.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Becomes ScheduleName when applied.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Structural-only parameters JSON (portable across companies). Never contains tenant IDs.</summary>
    public string? ParametersJson { get; set; }

    public string RecurrenceType { get; set; } = "Monthly";
    public int? RecurrenceDay { get; set; } = 1;
    public TimeSpan ScheduleTime { get; set; } = new(8, 0, 0);
    public string ExportFormat { get; set; } = "Excel";
    public bool IncludeAiAnalysis { get; set; }
    public string AiLocale { get; set; } = "en";
    public bool SkipIfEmpty { get; set; }

    /// <summary>Lowercase, alphanumeric-plus-hyphen slug of a display name; stable within a pack.</summary>
    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "item";
        var sb = new System.Text.StringBuilder(name.Length);
        var lastHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); lastHyphen = false; }
            else if (!lastHyphen) { sb.Append('-'); lastHyphen = true; }
        }
        return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "item";
    }
}

/// <summary>
/// Outcome of applying a pack to a company.
/// </summary>
public class TemplatePackApplyResult
{
    public bool Success { get; set; }
    public bool AlreadyApplied { get; set; }
    public int CreatedCount { get; set; }
    /// <summary>Items skipped because they were already applied to this company.</summary>
    public int SkippedCount { get; set; }
    public string? PackName { get; set; }
    public string? Message { get; set; }
}
