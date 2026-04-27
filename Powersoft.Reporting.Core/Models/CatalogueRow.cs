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
    public string? ItemInvoiceDescription { get; set; }

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

    // NOTE: The original Powersoft365 (VB.NET / repPowerReportCatalogue.aspx.vb, lines ~2222-2235) defines:
    //   Margin% = totalProfit / totalNetValue * 100
    //   Markup% = totalProfit / totalTransactionCost * 100
    // where "totalProfit" is the SUM of the per-row Profit column, and the per-row Profit column is
    // Quantity * ProfitBasePrice (NOT Revenue - Cost). We must mirror that exactly to stay 1:1 with
    // the legacy app. Do NOT subtract TransactionCost here.
    public decimal Margin => NetValue != 0
        ? Math.Round(ProfitValue / NetValue * 100, 2) : 0;

    public decimal Markup => TransactionCost != 0
        ? Math.Round(ProfitValue / TransactionCost * 100, 2) : 100;

    // Stock
    public decimal TotalStockQty { get; set; }
    public decimal TotalStockValue { get; set; }

    // Entity (customer/supplier)
    public string? EntityCode { get; set; }
    public string? EntityName { get; set; }
    public string? EntityShortName { get; set; }
    public string? EntityID { get; set; }
    public string? Sundry { get; set; }

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

    // Item master prices (excl VAT) — Price 1..10 from tbl_Item.
    // Mirrors legacy DisplayColumnE.Price1Excl..Price10Excl (repPowerReportCatalogue.aspx.vb:229-238).
    public decimal Price1Excl { get; set; }
    public decimal Price2Excl { get; set; }
    public decimal Price3Excl { get; set; }
    public decimal Price4Excl { get; set; }
    public decimal Price5Excl { get; set; }
    public decimal Price6Excl { get; set; }
    public decimal Price7Excl { get; set; }
    public decimal Price8Excl { get; set; }
    public decimal Price9Excl { get; set; }
    public decimal Price10Excl { get; set; }

    // Item master prices (incl VAT) — Price 1..10 from tbl_Item.
    public decimal Price1Incl { get; set; }
    public decimal Price2Incl { get; set; }
    public decimal Price3Incl { get; set; }
    public decimal Price4Incl { get; set; }
    public decimal Price5Incl { get; set; }
    public decimal Price6Incl { get; set; }
    public decimal Price7Incl { get; set; }
    public decimal Price8Incl { get; set; }
    public decimal Price9Incl { get; set; }
    public decimal Price10Incl { get; set; }

    // Per-line invoice price (the actual price on the invoice line, not the item master price).
    // Comes from d.ItemPriceExcl / d.ItemPriceIncl in legacy repPowerReportCatalogue.aspx.vb:4374-4395.
    // Detail mode only — meaningless in summary aggregates.
    public decimal InvPriceExcl { get; set; }
    public decimal InvPriceIncl { get; set; }

    // Customer categories (sale-only, from tbl_CustCategory via tbl_Customer.fk_Category1/2/3)
    public string? CustomerCategory1Descr { get; set; }
    public string? CustomerCategory2Descr { get; set; }
    public string? CustomerCategory3Descr { get; set; }

    // Custom attributes
    public string? ItemAttr1Descr { get; set; }
    public string? ItemAttr2Descr { get; set; }
    public string? ItemAttr3Descr { get; set; }
    public string? ItemAttr4Descr { get; set; }
    public string? ItemAttr5Descr { get; set; }
    public string? ItemAttr6Descr { get; set; }
}
