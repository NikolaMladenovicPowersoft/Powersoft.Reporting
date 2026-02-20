namespace Powersoft.Reporting.Core.Enums;

/// <summary>
/// How the report date range is determined when the schedule runs.
/// </summary>
public enum ReportDateRangeType
{
    LastNDays,
    ThisMonth,
    LastMonth,
    YearToDate,
    LastYear,
    Custom
}
