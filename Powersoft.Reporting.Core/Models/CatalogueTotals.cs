namespace Powersoft.Reporting.Core.Models;

public class CatalogueTotals
{
    public decimal TotalQuantity { get; set; }
    public decimal TotalValueBeforeDiscount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalNetValue { get; set; }
    public decimal TotalVatAmount { get; set; }
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalTransactionCost { get; set; }
    public decimal TotalTotalCost { get; set; }
    public decimal TotalStockQty { get; set; }
    public decimal TotalStockValue { get; set; }

    /// <summary>
    /// Sum of (Quantity * configured profit-base price) across all transaction legs.
    /// Matches the original Powersoft365 semantics: this is a reference value, not net profit.
    /// Read directly from SQL — set by the repository.
    /// </summary>
    public decimal TotalProfitValue { get; set; }

    // Mirror the legacy Powersoft365 formula (repPowerReportCatalogue.aspx.vb, lines ~2222-2235):
    //   Margin% = totalProfit / totalNetValue * 100
    //   Markup% = totalProfit / totalTransactionCost * 100
    // where totalProfit = SUM of per-row ProfitValue (= Quantity * ProfitBasePrice). Do NOT subtract cost.
    public decimal TotalMargin => TotalNetValue != 0
        ? Math.Round(TotalProfitValue / TotalNetValue * 100, 2) : 0;

    public decimal TotalMarkup => TotalTransactionCost != 0
        ? Math.Round(TotalProfitValue / TotalTransactionCost * 100, 2) : 100;
}
