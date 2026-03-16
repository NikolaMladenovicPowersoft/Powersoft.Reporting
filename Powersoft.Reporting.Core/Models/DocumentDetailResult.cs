namespace Powersoft.Reporting.Core.Models;

public class DocumentDetailResult
{
    public string DocType { get; set; } = "";
    public string DocTypeDescription { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public DateTime DocumentDate { get; set; }
    public string EntityCode { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string StoreCode { get; set; } = "";
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
    public List<DocumentLineItem> Lines { get; set; } = new();

    public static string GetDocTypeDescription(string kind) => kind switch
    {
        "P" => "Purchase Invoice",
        "E" => "Purchase Return",
        "I" => "Sales Invoice",
        "C" => "Credit Note",
        _ => kind
    };
}

public class DocumentLineItem
{
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
}
