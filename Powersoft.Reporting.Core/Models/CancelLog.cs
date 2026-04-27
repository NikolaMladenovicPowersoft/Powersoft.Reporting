namespace Powersoft.Reporting.Core.Models;

public class CancelLogDetailedRow
{
    public string StoreAndStation { get; set; } = "";
    public string Level1Code { get; set; } = "";
    public string Level1Descr { get; set; } = "";
    public string Level2Code { get; set; } = "";
    public string Level2Descr { get; set; } = "";
    public string ActionType { get; set; } = "";
    public DateTime? SessionDateTime { get; set; }
    public string StoreCode { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string StationCode { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string TransKind { get; set; } = "";
    public string CreditId { get; set; } = "";
    public string InvoiceId { get; set; } = "";
    public string CustomerCode { get; set; } = "";
    public string CustomerFullName { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemDescr { get; set; } = "";
    public string ZReport { get; set; } = "";
    public int TotalInvoiceLines { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public string TableNo { get; set; } = "";
    public string TableName { get; set; } = "";
    public int TableNoPerson { get; set; }
    public string CompartmentName { get; set; } = "";
}

public class CancelLogSummaryRow
{
    public string StoreAndStation { get; set; } = "";
    public string Level1Code { get; set; } = "";
    public string Level1Descr { get; set; } = "";
    public string Level2Code { get; set; } = "";
    public string Level2Descr { get; set; } = "";
    public int DeletedAction { get; set; }
    public int CancelledAction { get; set; }
    public int ComplimentaryAction { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}

public enum CancelLogActionType
{
    All,
    Deleted,
    Cancelled,
    Complimentary
}

public enum CancelLogReportType
{
    Detailed,
    Summary
}

public class CancelLogFilter
{
    public DateTime DateFrom { get; set; } = DateTime.Today;
    public DateTime DateTo { get; set; } = DateTime.Today;
    public bool ReportByDateTime { get; set; }
    public CancelLogActionType ActionType { get; set; } = CancelLogActionType.All;
    public CancelLogReportType ReportType { get; set; } = CancelLogReportType.Detailed;
    public string PrimaryGroup { get; set; } = "NONE";
    public string SecondaryGroup { get; set; } = "NONE";
    public int TimezoneOffsetMinutes { get; set; }
    public int MaxRecords { get; set; } = 50000;
    public ItemsSelectionFilter? ItemsSelection { get; set; }
    public string SortColumn { get; set; } = "SessionDateTime";
    public string SortDirection { get; set; } = "ASC";
}
