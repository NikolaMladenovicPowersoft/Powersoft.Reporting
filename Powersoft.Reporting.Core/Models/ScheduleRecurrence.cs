namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Outlook / SQL Agent style recurrence. Serialized to RecurrenceJson in tbl_ReportSchedule.
/// </summary>
public class ScheduleRecurrence
{
    public string Type { get; set; } = "Daily"; // Once, Daily, Weekly, Monthly
    public ScheduleRecurrencePattern? Pattern { get; set; }
    public string Time { get; set; } = "08:00"; // HH:mm
    public ScheduleRecurrenceRange? Range { get; set; }
}

public class ScheduleRecurrencePattern
{
    /// <summary>Every N days/weeks/months.</summary>
    public int Interval { get; set; } = 1;
    /// <summary>Weekly: 0=Sun, 1=Mon, ... 6=Sat. Multiple allowed.</summary>
    public int[]? DaysOfWeek { get; set; }
    /// <summary>Monthly: day of month (1-31).</summary>
    public int? DayOfMonth { get; set; }
    /// <summary>Monthly: 1=first, 2=second, ... 5=last weekday of month. Use with DayOfWeek.</summary>
    public int? WeekOfMonth { get; set; }
    /// <summary>Monthly "first Monday": 1=Mon, 2=Tue, ... 7=Sun. Null when using DayOfMonth only.</summary>
    public int? DayOfWeek { get; set; }
}

public class ScheduleRecurrenceRange
{
    public string StartDate { get; set; } = ""; // ISO date
    public string? EndDate { get; set; }
    public bool NoEndDate { get; set; } = true;
    public int? MaxOccurrences { get; set; }
}
