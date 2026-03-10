namespace Powersoft.Reporting.Core.Models;

public class TransactionDetailRow
{
    public DateTime DateTrans { get; set; }
    public string Kind { get; set; } = "";
    public string KindDescription { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string EntityCode { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string StoreCode { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }

    public static string GetKindDescription(string kind) => kind switch
    {
        "P" => "Purchase Invoice",
        "E" => "Purchase Return",
        "I" => "Sales Invoice",
        "C" => "Credit Note",
        _ => kind
    };
}
