namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// A single AI analysis event, logged centrally (psCentral) so usage can be
/// reported across ALL tenant databases for one Powersoft admin in one place.
/// </summary>
public class AiUsageLogEntry
{
    public string DBCode { get; set; } = string.Empty;
    public string? DBName { get; set; }
    public string? UserCode { get; set; }
    public string? ReportType { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }

    /// <summary>"Interactive" (user clicked Analyze) or "Scheduled" (background run).</summary>
    public string Source { get; set; } = "Interactive";
}

/// <summary>One aggregated row in a usage breakdown (per company / report / user).</summary>
public class AiUsageGroupRow
{
    public string Label { get; set; } = string.Empty;
    public int AnalysisCount { get; set; }
    public long TotalTokens { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>
/// Cross-tenant AI usage report for a period, broken down by company, report type and user.
/// </summary>
public class AiUsageReport
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public int TotalAnalyses { get; set; }
    public long TotalTokens { get; set; }
    public decimal TotalCost { get; set; }

    public List<AiUsageGroupRow> ByCompany { get; set; } = new();
    public List<AiUsageGroupRow> ByReport { get; set; } = new();
    public List<AiUsageGroupRow> ByUser { get; set; } = new();
}
