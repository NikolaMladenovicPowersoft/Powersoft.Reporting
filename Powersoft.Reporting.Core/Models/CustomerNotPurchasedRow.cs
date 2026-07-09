namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// One row of the "Items Not Purchased (by Customer) in X Days" report.
/// Report B per George's spec: pick a customer (or all) + optional item/category scope + a day threshold,
/// and list items whose most recent SALE (to that customer / to anyone) is older than the threshold,
/// including when it was last bought.
/// This report has NO legacy 365 equivalent — semantics are driven by George's verbal spec, not parity.
/// </summary>
public class CustomerNotPurchasedRow
{
    // Customer context (populated when a customer is selected or GroupBy = Customer; null for "all customers")
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }

    // Item identity — tbl_Item.pk_ItemID is BIGINT in CloudAccounting, so this must be long.
    public long ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    // Category context (for category grouping / display)
    public string? CategoryCode { get; set; }
    public string? CategoryDescr { get; set; }

    /// <summary>Most recent sale date within scope (to the selected customer, or overall). Null = never sold in scope.</summary>
    public DateTime? LastPurchaseDate { get; set; }

    /// <summary>Days between <see cref="LastPurchaseDate"/> and the report reference date. Null when never sold in scope.</summary>
    public int? DaysSinceLastPurchase { get; set; }

    /// <summary>Quantity on the most recent sale (context for "when it was last bought").</summary>
    public decimal LastPurchaseQty { get; set; }

    /// <summary>Total quantity sold in scope over the observation window (0 when never sold in scope).</summary>
    public decimal TotalQtyInWindow { get; set; }
}
