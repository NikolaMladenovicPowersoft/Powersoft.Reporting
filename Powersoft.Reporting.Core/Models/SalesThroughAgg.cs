namespace Powersoft.Reporting.Core.Models;

/// <summary>Running subtotal accumulator for Sales Through group breaks (exports/print).</summary>
public class SalesThroughAgg
{
    public decimal IntakeQty { get; private set; }
    public decimal IntakeValue { get; private set; }
    public decimal SalesQty { get; private set; }
    public decimal SalesNet { get; private set; }
    public decimal SalesGross { get; private set; }
    public decimal CurrentStock { get; private set; }

    public decimal SellThroughQtyPct => IntakeQty != 0
        ? Math.Round(SalesQty / IntakeQty * 100, 2) : (SalesQty != 0 ? 100 : 0);

    public decimal SellThroughValuePct => IntakeValue != 0
        ? Math.Round(SalesNet / IntakeValue * 100, 2) : (SalesNet != 0 ? 100 : 0);

    public void Add(SalesThroughRow row)
    {
        IntakeQty += row.IntakeQty;
        IntakeValue += row.IntakeValue;
        SalesQty += row.SalesQty;
        SalesNet += row.SalesNet;
        SalesGross += row.SalesGross;
        CurrentStock += row.CurrentStock;
    }
}
