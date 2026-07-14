namespace Powersoft.Reporting.Core.Models;

/// <summary>Grand totals for the Sales Through report (whole filtered set, not just the page).</summary>
public class SalesThroughTotals
{
    public decimal TotalIntakeQty { get; set; }
    public decimal TotalIntakeValue { get; set; }
    public decimal TotalSalesQty { get; set; }
    public decimal TotalSalesNet { get; set; }
    public decimal TotalSalesGross { get; set; }
    public decimal TotalCurrentStock { get; set; }

    public decimal SellThroughQtyPct => TotalIntakeQty != 0
        ? Math.Round(TotalSalesQty / TotalIntakeQty * 100, 2) : 100;

    public decimal SellThroughValuePct => TotalIntakeValue != 0
        ? Math.Round(TotalSalesNet / TotalIntakeValue * 100, 2) : 100;
}
