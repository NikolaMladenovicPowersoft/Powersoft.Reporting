namespace Powersoft.Reporting.Web.ViewModels;

/// <summary>
/// Drives the shared <c>_SaveLayout.cshtml</c> partial.
///
/// The host view MUST define two global JS hooks:
/// <list type="bullet">
///   <item><c>window.collectCurrentLayout()</c> — returns a plain object (key/value dict) of the
///   report's current filter parameters to persist.</item>
///   <item><c>window.applyLayoutParameters(params)</c> — restores form controls from a saved params
///   object returned by the server.</item>
/// </list>
/// </summary>
public class SaveLayoutPartialModel
{
    /// <summary>Report type constant (from <c>ReportTypeConstants</c>) — routed to generic endpoints.</summary>
    public string ReportType { get; set; } = "";

    /// <summary>
    /// When true the "Reset Default" button is rendered (meaning a default layout has already been saved).
    /// </summary>
    public bool HasSavedLayout { get; set; }

    /// <summary>URL to redirect to after a successful Reset (typically the report's GET action URL).</summary>
    public string ReportViewUrl { get; set; } = "";
}
