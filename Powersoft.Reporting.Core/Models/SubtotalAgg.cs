namespace Powersoft.Reporting.Core.Models;

public class SubtotalAgg
{
    public decimal QtyPurchased { get; private set; }
    public decimal ValPurchased { get; private set; }
    public decimal QtySold { get; private set; }
    public decimal ValSold { get; private set; }
    public decimal Profit { get; private set; }
    public decimal StockQty { get; private set; }

    public decimal QtyPct => QtyPurchased != 0 ? Math.Round(QtySold / QtyPurchased * 100, 2) : (QtySold != 0 ? 100 : 0);
    public decimal ValPct => ValPurchased != 0 ? Math.Round(ValSold / ValPurchased * 100, 2) : (ValSold != 0 ? 100 : 0);

    public void Add(PurchasesSalesRow row, bool includeVat)
    {
        QtyPurchased += row.QuantityPurchased;
        ValPurchased += includeVat ? row.GrossPurchasedValue : row.NetPurchasedValue;
        QtySold += row.QuantitySold;
        ValSold += includeVat ? row.GrossSoldValue : row.NetSoldValue;
        Profit += row.Profit;
        StockQty += row.TotalStockQty;
    }
}
