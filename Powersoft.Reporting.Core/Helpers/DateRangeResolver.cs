using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Resolves a ReportDateRangeOption into concrete DateFrom/DateTo relative to a reference date (typically DateTime.Today).
/// Used by the scheduler runner so reports always cover the intended window regardless of when they execute.
/// </summary>
public static class DateRangeResolver
{
    public static (DateTime DateFrom, DateTime DateTo) Resolve(ReportDateRangeOption? option, DateTime? referenceDate = null)
    {
        var today = (referenceDate ?? DateTime.Today).Date;

        if (option == null)
            return (today.AddDays(-29), today);

        return option.Type switch
        {
            ReportDateRangeType.LastNDays =>
                (today.AddDays(-(Math.Max(option.Value, 1) - 1)), today),

            ReportDateRangeType.Yesterday =>
                (today.AddDays(-1), today.AddDays(-1)),

            ReportDateRangeType.ThisWeek => ResolveThisWeek(today),

            ReportDateRangeType.LastWeek => ResolveLastWeek(today),

            ReportDateRangeType.ThisMonth =>
                (new DateTime(today.Year, today.Month, 1), today),

            ReportDateRangeType.LastMonth =>
                (new DateTime(today.Year, today.Month, 1).AddMonths(-1),
                 new DateTime(today.Year, today.Month, 1).AddDays(-1)),

            ReportDateRangeType.YearToDate =>
                (new DateTime(today.Year, 1, 1), today),

            ReportDateRangeType.LastYear =>
                (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31)),

            ReportDateRangeType.Custom => ResolveCustom(option, today),

            _ => (today.AddDays(-29), today)
        };
    }

    /// <summary>
    /// Monday through today (week starts on Monday — ISO/European convention).
    /// </summary>
    private static (DateTime, DateTime) ResolveThisWeek(DateTime today)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        return (monday, today);
    }

    /// <summary>
    /// Previous full week: Monday to Sunday.
    /// </summary>
    private static (DateTime, DateTime) ResolveLastWeek(DateTime today)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-daysSinceMonday);
        var lastMonday = thisMonday.AddDays(-7);
        var lastSunday = thisMonday.AddDays(-1);
        return (lastMonday, lastSunday);
    }

    private static (DateTime, DateTime) ResolveCustom(ReportDateRangeOption option, DateTime today)
    {
        var from = DateTime.TryParse(option.DateFrom, out var parsedFrom) ? parsedFrom.Date : today.AddDays(-29);
        var to = DateTime.TryParse(option.DateTo, out var parsedTo) ? parsedTo.Date : today;
        return (from, to);
    }
}
