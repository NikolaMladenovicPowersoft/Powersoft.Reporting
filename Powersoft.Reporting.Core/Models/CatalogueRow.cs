namespace Powersoft.Reporting.Core.Models;

public class CatalogueRow
{
    // Grouping levels
    public string? Level1 { get; set; }
    public string? Level1Value { get; set; }
    public string? Level2 { get; set; }
    public string? Level2Value { get; set; }
    public string? Level3 { get; set; }
    public string? Level3Value { get; set; }

    // Item identification
    public string? ItemCode { get; set; }
    public string? MainBarcode { get; set; }
    public string? ItemDescription { get; set; }

    // Amounts
    public decimal Quantity { get; set; }
    public decimal ValueBeforeDiscount { get; set; }
    public decimal Discount { get; set; }
    public decimal NetValue { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }

    // Profitability
    /// <summary>
    /// Reference value: Quantity * configured profit-base price (e.g. Price1Excl).
    /// This is NOT net profit; it matches the original Powersoft365 column semantics.
    /// </summary>
    public decimal ProfitValue { get; set; }
    public decimal Cost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TransactionCost { get; set; }

    public decimal Margin => NetValue != 0
        ? Math.Round((ProfitValue - TransactionCost) / NetValue * 100, 2) : 0;

    public decimal Markup => TransactionCost != 0
        ? Math.Round((ProfitValue - TransactionCost) / TransactionCost * 100, 2) : 100;

    // Stock
    public decimal TotalStockQty { get; set; }
    public decimal TotalStockValue { get; set; }

    // Entity (customer/supplier)
    public string? EntityCode { get; set; }
    public string? EntityName { get; set; }

    // Invoice
    public string? InvoiceNumber { get; set; }
    public string? InvoiceType { get; set; }
    public string? PaymentType { get; set; }
    public string? TransactionNumber { get; set; }

    // Location
    public string? StoreCode { get; set; }
    public string? StoreName { get; set; }
    public string? StationCode { get; set; }
    public string? StationName { get; set; }
    public string? FranchiseName { get; set; }

    // Time
    public DateTime? DateTrans { get; set; }
    public string? UserCode { get; set; }
    public string? AgentName { get; set; }
    public string? ZReportNumber { get; set; }

    // Item classification
    public string? ItemCategoryCode { get; set; }
    public string? ItemCategoryDescr { get; set; }
    public string? ItemDepartmentCode { get; set; }
    public string? ItemDepartmentDescr { get; set; }
    public string? ModelCode { get; set; }
    public string? Colour { get; set; }
    public string? Size { get; set; }
    public string? BrandName { get; set; }
    public string? SeasonName { get; set; }
    public string? ItemSupplierCode { get; set; }
    public string? ItemSupplierName { get; set; }

    // Prices (excl VAT)
    public decimal Price1Excl { get; set; }
    public decimal Price2Excl { get; set; }
    public decimal Price3Excl { get; set; }

    // Prices (incl VAT)
    public decimal Price1Incl { get; set; }
    public decimal Price2Incl { get; set; }
    public decimal Price3Incl { get; set; }

    // Custom attributes
    public string? ItemAttr1Descr { get; set; }
    public string? ItemAttr2Descr { get; set; }
    public string? ItemAttr3Descr { get; set; }
    public string? ItemAttr4Descr { get; set; }
    public string? ItemAttr5Descr { get; set; }
    public string? ItemAttr6Descr { get; set; }
}
