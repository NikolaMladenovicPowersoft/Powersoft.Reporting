namespace Powersoft.Reporting.Web.ViewModels;

/// <summary>
/// Drives the shared <c>_Schedule.cshtml</c> partial. The schedule modal is otherwise generic;
/// only the persistence endpoints, the report badge and a couple of placeholders differ per report.
///
/// The host view MUST define a global JS function <c>collectScheduleParameters()</c> that returns
/// the report-specific parameters object (it will be serialised into <c>parametersJson</c>). The
/// partial merges in <c>reportDateRange</c> automatically, so the host need not include it.
/// </summary>
public class SchedulePartialModel
{
    /// <summary>Controller action that persists the schedule (e.g. "SaveParetoSchedule").</summary>
    public string SaveAction { get; set; } = "";

    /// <summary>Controller action that lists existing schedules for this report (e.g. "GetParetoSchedules").</summary>
    public string ListAction { get; set; } = "";

    /// <summary>Badge text shown in the existing-schedules table (e.g. "Pareto 80/20").</summary>
    public string ReportBadge { get; set; } = "Report";

    /// <summary>Placeholder text for the schedule name input.</summary>
    public string NamePlaceholder { get; set; } = "e.g. Weekly Report";

    /// <summary>Placeholder text for the email subject input.</summary>
    public string SubjectPlaceholder { get; set; } = "Report - {date}";

    /// <summary>Default recipients pre-filled when the form resets.</summary>
    public string DefaultRecipients { get; set; } = "gm@powersoft.com.cy";

    /// <summary>Default value for the custom "From" date input (yyyy-MM-dd).</summary>
    public string DefaultDateFrom { get; set; } = "";

    /// <summary>Default value for the custom "To" date input (yyyy-MM-dd).</summary>
    public string DefaultDateTo { get; set; } = "";
}
