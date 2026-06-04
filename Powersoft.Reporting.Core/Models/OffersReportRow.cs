namespace Powersoft.Reporting.Core.Models;

public class OffersReportRow
{
    public string OfferNo { get; set; } = "";
    public string Level1Code { get; set; } = "";
    public string Level1Descr { get; set; } = "";
    public string Level2Code { get; set; } = "";
    public string Level2Descr { get; set; } = "";
    public string Level3Code { get; set; } = "";
    public string Level3Descr { get; set; } = "";
    public string StatusName { get; set; } = "";
    public string StatusColor { get; set; } = "";
    public DateTime? DateTrans { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerCode { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string StoreCode { get; set; } = "";
    public string AgentName { get; set; } = "";
    public decimal InvoiceTotal { get; set; }
    public decimal InvoiceVat { get; set; }
    public decimal InvoiceTotalDiscount { get; set; }
    public decimal InvoiceGrandTotal { get; set; }
    public decimal InvoiceDiscountPerc { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalItemCost { get; set; }
    public string Comments { get; set; } = "";
    public string InternalNotes { get; set; } = "";
    public bool Printed { get; set; }
    public bool SentByEmail { get; set; }
    public bool IsStandardOffer { get; set; }
    public string StandardOfferName { get; set; } = "";
    public decimal OrderPercentage { get; set; }
    public string LinkedLead { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string Source { get; set; } = "Offer";
}
