namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Group-level subtotal accumulator for the Power Reports Catalogue.
///
/// Mirrors the on-screen subtotal math 1:1 (see Catalogue.cshtml renderSubtotal) so that the
/// grid, print preview and all exports (Excel/CSV/PDF) produce identical numbers:
///   - numeric columns are summed,
///   - Markup% / Margin% are RECOMPUTED from the summed components (never averaged),
///   - Cost and TotalStockValue are left blank in subtotals (matches the grid),
///   - Markup/Margin fall back to 0 (not 100) at subtotal level (matches the grid).
/// </summary>
public sealed class CatalogueSubtotal
{
    public decimal Quantity { get; private set; }
    public decimal ValueBeforeDiscount { get; private set; }
    public decimal Discount { get; private set; }
    public decimal NetValue { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal ProfitValue { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal TransactionCost { get; private set; }
    public decimal TotalStockQty { get; private set; }
    public decimal TotalStockValue { get; private set; }
    public int Count { get; private set; }

    public decimal Margin => NetValue != 0 ? System.Math.Round(ProfitValue / NetValue * 100m, 2) : 0m;
    public decimal Markup => TransactionCost != 0 ? System.Math.Round(ProfitValue / TransactionCost * 100m, 2) : 0m;

    public void Add(CatalogueRow r)
    {
        Quantity += r.Quantity;
        ValueBeforeDiscount += r.ValueBeforeDiscount;
        Discount += r.Discount;
        NetValue += r.NetValue;
        VatAmount += r.VatAmount;
        GrossAmount += r.GrossAmount;
        ProfitValue += r.ProfitValue;
        TotalCost += r.TotalCost;
        TransactionCost += r.TransactionCost;
        TotalStockQty += r.TotalStockQty;
        TotalStockValue += r.TotalStockValue;
        Count++;
    }

    public static CatalogueSubtotal From(System.Collections.Generic.IEnumerable<CatalogueRow> rows)
    {
        var s = new CatalogueSubtotal();
        foreach (var r in rows) s.Add(r);
        return s;
    }

    /// <summary>
    /// Value to render for a given display-column key in a subtotal row, or null if the column
    /// must be left blank in subtotals (group/item/text columns, Cost, TotalStockValue).
    /// Keys match the export column keys / DisplayColumns names.
    /// </summary>
    public decimal? ValueForKey(string key)
    {
        switch (key)
        {
            case "Quantity": return Quantity;
            case "Value": return ValueBeforeDiscount;
            case "Discount": return Discount;
            case "NetValue": return NetValue;
            case "VatAmount": return VatAmount;
            case "GrossAmount": return GrossAmount;
            case "Profit": return ProfitValue;
            case "Markup": return Markup;
            case "Margin": return Margin;
            case "TotalCost": return TotalCost;
            case "TotalStockQty": return TotalStockQty;
            // Cost and TotalStockValue are intentionally blank in subtotals (matches the grid).
            default: return null;
        }
    }
}
