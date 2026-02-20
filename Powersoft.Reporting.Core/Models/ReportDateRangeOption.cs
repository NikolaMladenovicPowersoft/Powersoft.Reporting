using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Stored in ParametersJson.reportDateRange. When the schedule runs, dates are computed from this (e.g. last 7 days from run date).
/// </summary>
public class ReportDateRangeOption
{
    public ReportDateRangeType Type { get; set; }
    /// <summary>For LastNDays: number of days (e.g. 7). Ignored for other types.</summary>
    public int Value { get; set; }
    /// <summary>For Custom: fixed from date (ISO).</summary>
    public string? DateFrom { get; set; }
    /// <summary>For Custom: fixed to date (ISO).</summary>
    public string? DateTo { get; set; }
}
