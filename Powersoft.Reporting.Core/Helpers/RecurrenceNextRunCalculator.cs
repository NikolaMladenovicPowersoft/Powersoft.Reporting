using System.Text.Json;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Computes NextRunDate from RecurrenceJson (Outlook/SQL Agent style).
/// </summary>
public static class RecurrenceNextRunCalculator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static DateTime? GetNextRun(string? recurrenceJson, DateTime fromWhen)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson))
            return null;

        try
        {
            var r = JsonSerializer.Deserialize<ScheduleRecurrence>(recurrenceJson, JsonOptions);
            if (r == null) return null;

            if (!TimeSpan.TryParse(r.Time, out var timeOfDay))
                timeOfDay = new TimeSpan(8, 0, 0);

            var startDate = DateTime.MinValue;
            if (r.Range != null && !string.IsNullOrEmpty(r.Range.StartDate) && DateTime.TryParse(r.Range.StartDate, out var sd))
                startDate = sd.Date;

            var effectiveFrom = fromWhen < startDate ? startDate : fromWhen;
            var todayAtTime = effectiveFrom.Date.Add(timeOfDay);

            switch (r.Type?.ToLowerInvariant())
            {
                case "once":
                    var onceAt = startDate.Add(timeOfDay);
                    return onceAt > fromWhen ? onceAt : null;
                case "daily":
                    return NextDaily(effectiveFrom, todayAtTime, r.Pattern?.Interval ?? 1);
                case "weekly":
                    return NextWeekly(effectiveFrom, timeOfDay, r.Pattern);
                case "monthly":
                    return NextMonthly(effectiveFrom, timeOfDay, r.Pattern);
                default:
                    return NextDaily(effectiveFrom, todayAtTime, 1);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the single occurrence date/time for "Once" type, or null if not Once or invalid.
    /// Used for validation: do not allow past date/time for Run once.
    /// </summary>
    public static DateTime? GetOnceScheduleDateTime(string? recurrenceJson)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson)) return null;
        try
        {
            var r = JsonSerializer.Deserialize<ScheduleRecurrence>(recurrenceJson, JsonOptions);
            if (r == null || !string.Equals(r.Type, "Once", StringComparison.OrdinalIgnoreCase)) return null;
            if (r.Range == null || string.IsNullOrEmpty(r.Range.StartDate) || !DateTime.TryParse(r.Range.StartDate, out var startDate))
                return null;
            if (!TimeSpan.TryParse(r.Time, out var timeOfDay))
                timeOfDay = new TimeSpan(8, 0, 0);
            return startDate.Date.Add(timeOfDay);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? NextDaily(DateTime fromWhen, DateTime todayAtTime, int intervalDays)
    {
        if (todayAtTime > fromWhen)
            return todayAtTime;
        return fromWhen.Date.AddDays(intervalDays).Add(todayAtTime.TimeOfDay);
    }

    private static DateTime? NextWeekly(DateTime fromWhen, TimeSpan timeOfDay, ScheduleRecurrencePattern? pattern)
    {
        var daysOfWeek = pattern?.DaysOfWeek ?? new[] { (int)fromWhen.DayOfWeek };
        DateTime? nextInWeek = null;
        foreach (var dow in daysOfWeek.OrderBy(d => d))
        {
            var next = NextWeekday(fromWhen, (DayOfWeek)(dow % 7), timeOfDay);
            if (next.HasValue && next > fromWhen && (nextInWeek == null || next < nextInWeek))
                nextInWeek = next;
        }
        if (nextInWeek.HasValue) return nextInWeek;
        var firstDow = (DayOfWeek)(daysOfWeek.Min() % 7);
        return NextWeekday(fromWhen.Date.AddDays(7), firstDow, timeOfDay);
    }

    private static DateTime? NextWeekday(DateTime fromWhen, DayOfWeek dayOfWeek, TimeSpan timeOfDay)
    {
        var daysUntil = ((int)dayOfWeek - (int)fromWhen.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && fromWhen.TimeOfDay >= timeOfDay) daysUntil = 7;
        var d = fromWhen.Date.AddDays(daysUntil).Add(timeOfDay);
        return d > fromWhen ? d : null;
    }

    private static DateTime? NextMonthly(DateTime fromWhen, TimeSpan timeOfDay, ScheduleRecurrencePattern? pattern)
    {
        if (pattern?.WeekOfMonth != null && pattern.DayOfWeek != null)
        {
            var nth = pattern.WeekOfMonth.Value;
            var dow = (DayOfWeek)(pattern.DayOfWeek % 7);
            var candidate = NthWeekdayOfMonth(fromWhen.Year, fromWhen.Month, nth, dow).Add(timeOfDay);
            if (candidate > fromWhen) return candidate;
            var nextMonth = fromWhen.Month == 12 ? (fromWhen.Year + 1, 1) : (fromWhen.Year, fromWhen.Month + 1);
            return NthWeekdayOfMonth(nextMonth.Item1, nextMonth.Item2, nth, dow).Add(timeOfDay);
        }
        var dayOfMonth = pattern?.DayOfMonth ?? 1;
        var maxDay = DateTime.DaysInMonth(fromWhen.Year, fromWhen.Month);
        var day = Math.Min(dayOfMonth, maxDay);
        var candidate2 = new DateTime(fromWhen.Year, fromWhen.Month, day).Add(timeOfDay);
        if (candidate2 > fromWhen) return candidate2;
        var next = fromWhen.Month == 12
            ? new DateTime(fromWhen.Year + 1, 1, Math.Min(dayOfMonth, 31)).Add(timeOfDay)
            : new DateTime(fromWhen.Year, fromWhen.Month + 1, Math.Min(dayOfMonth, DateTime.DaysInMonth(fromWhen.Year, fromWhen.Month + 1))).Add(timeOfDay);
        return next;
    }

    private static DateTime NthWeekdayOfMonth(int year, int month, int n, DayOfWeek dayOfWeek)
    {
        var first = new DateTime(year, month, 1);
        var firstDow = (int)first.DayOfWeek;
        var target = (int)dayOfWeek;
        var offset = (target - firstDow + 7) % 7;
        var firstOccurrence = first.AddDays(offset);
        if (n == 5)
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            while (last.DayOfWeek != dayOfWeek) last = last.AddDays(-1);
            return last;
        }
        return firstOccurrence.AddDays(7 * (n - 1));
    }
}
