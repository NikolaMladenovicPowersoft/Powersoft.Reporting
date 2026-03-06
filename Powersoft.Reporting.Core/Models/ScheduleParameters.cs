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
    public string? ReportType { get; set; }

    // Shared
    public bool IncludeVat { get; set; }
    public List<string>? StoreCodes { get; set; }
    public List<int>? ItemIds { get; set; }

    /// <summary>
    /// Relative date range resolved at execution time.
    /// If null, defaults to LastNDays with Value=30.
    /// </summary>
    public ReportDateRangeOption? DateRange { get; set; }

    public string SortColumn { get; set; } = "Period";
    public string SortDirection { get; set; } = "ASC";
}
