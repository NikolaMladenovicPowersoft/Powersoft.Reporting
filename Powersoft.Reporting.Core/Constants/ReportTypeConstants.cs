namespace Powersoft.Reporting.Core.Constants;

/// <summary>
/// Single source of truth for report type identifiers.
/// Coordinates with tbl_ReportSchedule.ReportType column (NVARCHAR).
/// When adding a new schedulable report: add a constant here and include it in Schedulable.
/// </summary>
public static class ReportTypeConstants
{
    public const string AverageBasket = "AverageBasket";
    public const string PurchasesSales = "PurchasesSales";
    public const string BelowMinStock = "BelowMinStock";
    public const string Catalogue = "Catalogue";
    public const string CancelLog = "CancelLog";
    public const string Pareto = "Pareto";
    public const string Charts = "Charts";

    /// <summary>
    /// The set of report types that can be scheduled.
    /// Used for validation when creating/updating schedules.
    /// </summary>
    public static readonly HashSet<string> Schedulable = new(StringComparer.OrdinalIgnoreCase)
    {
        AverageBasket,
        PurchasesSales,
        BelowMinStock,
        Catalogue,
        CancelLog,
        Pareto,
        Charts
    };

    public static bool IsSchedulable(string reportType) =>
        !string.IsNullOrWhiteSpace(reportType) && Schedulable.Contains(reportType);
}
