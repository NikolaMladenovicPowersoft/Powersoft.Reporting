namespace Powersoft.Reporting.Core.Models;

public class OffersReportFilter
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public string DateField { get; set; } = "DateTrans";
    public string StatusFilter { get; set; } = "All";
    public string StoreFilter { get; set; } = "All";
    public string AgentFilter { get; set; } = "All";
    public string PrimaryGroup { get; set; } = "NONE";
    public string SecondaryGroup { get; set; } = "NONE";
    public string ThirdGroup { get; set; } = "NONE";
    public int MaxRecords { get; set; } = 50000;
    public string SortColumn { get; set; } = "DateTrans";
    public string SortDirection { get; set; } = "DESC";
    public string OfferType { get; set; } = "All";
    public bool IncludeHistory { get; set; }
    public List<string> CustomerCodes { get; set; } = new();
    public bool CustomerExcludeMode { get; set; } = false;

    // Multi-select — when non-empty, take precedence over the single-value filters above.
    public List<string> StatusCodes { get; set; } = new();
    public List<string> StoreCodes { get; set; } = new();
    public List<string> AgentCodes { get; set; } = new();

    // Items selection (from _ItemsSelection partial) — JSON string
    public string? ItemsSelectionJson { get; set; }
}
