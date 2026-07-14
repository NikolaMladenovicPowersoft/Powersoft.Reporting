namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// One row of the Sales Through (sell-through) report — Splash/George 2026-07.
/// Mirrors the "SS26 SALES BY LINE" sheet of the Splash Excel workbook
/// (ERES SS26 ST Report), which itself is built on the legacy
/// Power Purchases &amp; Sales export:
///   Intake  = net purchases (invoices - returns) in the period
///   Sales   = net sales (invoices - credits) in the period
///   Sell-through % (qty)   = SalesQty / IntakeQty * 100
///   Sell-through % (value) = SalesNet / IntakeValue * 100
///   Mix %   = row share of the report grand total (qty based)
/// </summary>
public class SalesThroughRow
{
    public string? Level1 { get; set; }
    public string? Level1Value { get; set; }
    public string? Level2 { get; set; }
    public string? Level2Value { get; set; }
    public string? Level3 { get; set; }
    public string? Level3Value { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    /// <summary>Net purchases quantity (purchase invoices minus purchase returns).</summary>
    public decimal IntakeQty { get; set; }

    /// <summary>Net purchases cost value ("Net Purchases CV" in the Splash sheet).</summary>
    public decimal IntakeValue { get; set; }

    public decimal SalesQty { get; set; }
    public decimal SalesNet { get; set; }
    public decimal SalesGross { get; set; }

    /// <summary>Current stock snapshot (tbl_Item.TotalStockQty / per-store stock).</summary>
    public decimal CurrentStock { get; set; }

    // Sell-through — same zero-denominator convention as PurchasesSalesRow (0 intake -> 100%),
    // which matches the legacy export the Splash workbook is built from.
    public decimal SellThroughQtyPct => IntakeQty != 0
        ? Math.Round(SalesQty / IntakeQty * 100, 2) : 100;

    public decimal SellThroughValuePct => IntakeValue != 0
        ? Math.Round(SalesNet / IntakeValue * 100, 2) : 100;

    // Mix % of the report grand total (computed by the repository once totals are known).
    public decimal SalesMixPct { get; set; }
    public decimal IntakeMixPct { get; set; }
    public decimal StockMixPct { get; set; }
}
