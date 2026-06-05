namespace Powersoft.Reporting.Web.ViewModels;

/// <summary>
/// Drives the shared <c>_SendEmail.cshtml</c> partial. The send-email modal is otherwise generic;
/// only the persistence endpoint, the report type (for template filtering) and a default subject
/// differ per report.
///
/// The host view MUST define a global JS function <c>collectEmailParameters()</c> that returns the
/// report-specific parameters object (the same shape the report posts when generating / exporting),
/// or <c>null</c> to abort (e.g. when no data has been generated yet — the host shows its own alert).
/// </summary>
public class SendEmailPartialModel
{
    /// <summary>Controller action that sends the email (e.g. "SendOffersReportEmail").</summary>
    public string SendAction { get; set; } = "";

    /// <summary>Report type constant — used to filter email templates (global + report-specific).</summary>
    public string ReportType { get; set; } = "";

    /// <summary>Default email subject pre-filled when the modal opens.</summary>
    public string SubjectDefault { get; set; } = "Report";

    /// <summary>
    /// Report type used when loading templates. Defaults to <see cref="ReportType"/> when empty.
    /// </summary>
    public string TemplatesReportType { get; set; } = "";
}
