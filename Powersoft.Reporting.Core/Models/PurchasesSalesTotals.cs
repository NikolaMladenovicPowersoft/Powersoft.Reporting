namespace Powersoft.Reporting.Core.Models;

public class PurchasesSalesTotals
{
    public decimal TotalQtyPurchased { get; set; }
    public decimal TotalNetPurchased { get; set; }
    public decimal TotalGrossPurchased { get; set; }

    public decimal TotalQtySold { get; set; }
    public decimal TotalNetSold { get; set; }
    public decimal TotalGrossSold { get; set; }

    public decimal TotalProfit => TotalNetSold - TotalNetPurchased;
    public decimal TotalStockQty { get; set; }

    public decimal QtyPercent => TotalQtyPurchased != 0
        ? Math.Round(TotalQtySold / TotalQtyPurchased * 100, 2) : 100;
    public decimal ValPercent => TotalNetPurchased != 0
        ? Math.Round(TotalNetSold / TotalNetPurchased * 100, 2) : 100;
}
