using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Deserialized from tbl_ReportSchedule.ParametersJson.
/// Contains everything needed to reproduce a report without a user session.
/// </summary>
public class ScheduleParameters
{
    // Average Basket specific
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    public GroupByType SecondaryGroupBy { get; set; } = GroupByType.None;
    public bool CompareLastYear { get; set; }

    // Purchases vs Sales specific
    public PsReportMode ReportMode { get; set; } = PsReportMode.Detailed;
    public PsGroupBy PrimaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy SecondaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy ThirdGroup { get; set; } = PsGroupBy.None;
    public bool ShowProfit { get; set; } = true;
    public bool ShowStock { get; set; }
    public bool ShowOnOrder { get; set; }
    public bool ShowReservation { get; set; }
    public bool ShowAvailable { get; set; }
    public bool IncludeAdditionalCharges { get; set; } = true;
    public string? ReportType { get; set; }

    // Pareto 80/20 specific
    public string? ParetoDimension { get; set; }
    public string? ParetoMetric { get; set; }
    public int ProfitBasis { get; set; }
    public bool ExcludeNegativeAmounts { get; set; } = true;
    public decimal ClassAThreshold { get; set; } = 80;
    public decimal ClassBThreshold { get; set; } = 95;
    public decimal PriceInterval { get; set; } = 10;
    public int PriceOnIndex { get; set; }
    public bool PriceOnIncludesVat { get; set; }
    public int TimezoneOffsetMinutes { get; set; }

    // Charts specific (keys from collectChartParams() in Charts.cshtml)
    public string? ChartMode { get; set; }
    public string? ChartDimension { get; set; }
    public string? ChartMetric { get; set; }
    public int TopN { get; set; } = 10;
    public string? ChartType { get; set; }
    public bool ShowOthers { get; set; } = true;

    // CancelLog specific (distinct keys to avoid clashing with PS primaryGroup/reportType)
    public string? CancelActionType { get; set; }
    public string? CancelLogReportType { get; set; }
    public string? CancelLogPrimaryGroup { get; set; }
    public string? CancelLogSecondaryGroup { get; set; }
    public bool ReportByDateTime { get; set; }
    public int MaxRecords { get; set; } = 50000;

    // Shared
    public bool IncludeVat { get; set; }
    public List<string>? StoreCodes { get; set; }
    public List<int>? ItemIds { get; set; }

    /// <summary>
    /// Full dimension filter JSON from _ItemsSelection.cshtml.
    /// Parsed at execution time via <see cref="Helpers.ItemsSelectionParser"/>.
    /// </summary>
    public string? ItemsSelectionJson { get; set; }

    /// <summary>
    /// Relative date range resolved at execution time.
    /// If null, defaults to LastNDays with Value=30.
    /// </summary>
    public ReportDateRangeOption? DateRange { get; set; }

    public string SortColumn { get; set; } = "Period";
    public string SortDirection { get; set; } = "ASC";
}
