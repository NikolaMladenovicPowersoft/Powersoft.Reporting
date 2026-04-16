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

    public decimal TotalProfitValue => TotalNetValue - TotalTransactionCost;

    public decimal TotalMargin => TotalNetValue != 0
        ? Math.Round(TotalProfitValue / TotalNetValue * 100, 2) : 0;

    public decimal TotalMarkup => TotalTransactionCost != 0
        ? Math.Round(TotalProfitValue / TotalTransactionCost * 100, 2) : 100;
}
