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
    public const string ProspectClients = "ProspectClients";
    public const string OffersReport = "OffersReport";
    public const string TrialBalance = "TrialBalance";
    public const string ProfitLoss = "ProfitLoss";

    // Report B (George, 2026): "Items Not Purchased (by Customer) in X Days".
    // Scheduler wiring (ScheduleExecutionService case + Cnp* params) is in place, so it is Schedulable.
    public const string CustomerNotPurchased = "CustomerNotPurchased";

    // Cash Flow (Direct) — George/Marinos 2026. Mirrors the Power BI cash-flow engine
    // (GetAllTransactionsForBowerBI IsCashFlow=1 + GetFullTreeCoaBI hierarchy) but self-contained.
    public const string CashFlow = "CashFlow";

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
        Charts,
        ProspectClients,
        OffersReport,
        TrialBalance,
        ProfitLoss,
        CustomerNotPurchased,
        CashFlow
    };

    public static bool IsSchedulable(string reportType) =>
        !string.IsNullOrWhiteSpace(reportType) && Schedulable.Contains(reportType);
}
