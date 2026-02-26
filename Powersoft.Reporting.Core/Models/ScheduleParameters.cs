using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Deserialized from tbl_ReportSchedule.ParametersJson.
/// Contains everything needed to reproduce a report without a user session.
/// </summary>
public class ScheduleParameters
{
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    public GroupByType SecondaryGroupBy { get; set; } = GroupByType.None;
    public bool IncludeVat { get; set; }
    public bool CompareLastYear { get; set; }
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
