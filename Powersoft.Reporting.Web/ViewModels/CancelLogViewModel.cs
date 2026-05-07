using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class CancelLogViewModel
{
    public DateTime DateFrom { get; set; } = DateTime.Today;
    public DateTime DateTo { get; set; } = DateTime.Today;
    public bool ReportByDateTime { get; set; }
    public string ActionType { get; set; } = "All";
    public string ReportType { get; set; } = "Detailed";
    public string PrimaryGroup { get; set; } = "NONE";
    public string SecondaryGroup { get; set; } = "NONE";
    public int TimezoneOffsetMinutes { get; set; }
    public string SortColumn { get; set; } = "SessionDateTime";
    public string SortDirection { get; set; } = "ASC";

    public List<CancelLogDetailedRow> DetailedRows { get; set; } = new();
    public List<CancelLogSummaryRow> SummaryRows { get; set; } = new();
    public int TotalCount { get; set; }

    public string? ConnectedDatabase { get; set; }

    public bool IsDetailed => ReportType.Equals("Detailed", StringComparison.OrdinalIgnoreCase);
    public bool HasPrimaryGroup => !string.IsNullOrEmpty(PrimaryGroup) && PrimaryGroup != "NONE";
    public bool HasSecondaryGroup => !string.IsNullOrEmpty(SecondaryGroup) && SecondaryGroup != "NONE";

    public string ActionTypeLabel => ActionType switch
    {
        "Deleted" => "Deleted Lines",
        "Cancelled" => "Cancelled Invoices",
        "Complimentary" => "Complimentary Items",
        _ => "All Actions"
    };
}
