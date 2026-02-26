namespace Powersoft.Reporting.Core.Constants;

/// <summary>
/// Single source of truth for report type identifiers.
/// Coordinates with tbl_ReportSchedule.ReportType column (NVARCHAR).
/// When adding a new schedulable report: add a constant here and include it in Schedulable.
/// </summary>
public static class ReportTypeConstants
{
    public const string AverageBasket = "AverageBasket";

    /// <summary>
    /// The set of report types that can be scheduled.
    /// Used for validation when creating/updating schedules.
    /// </summary>
    public static readonly HashSet<string> Schedulable = new(StringComparer.OrdinalIgnoreCase)
    {
        AverageBasket
    };

    public static bool IsSchedulable(string reportType) =>
        !string.IsNullOrWhiteSpace(reportType) && Schedulable.Contains(reportType);
}
