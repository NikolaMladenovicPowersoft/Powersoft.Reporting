namespace Powersoft.Reporting.Core.Models;

public class PurchasesSalesRow
{
    public string? Level1 { get; set; }
    public string? Level1Value { get; set; }
    public string? Level2 { get; set; }
    public string? Level2Value { get; set; }
    public string? Level3 { get; set; }
    public string? Level3Value { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    public decimal QuantityPurchased { get; set; }
    public decimal NetPurchasedValue { get; set; }
    public decimal GrossPurchasedValue { get; set; }

    public decimal QuantitySold { get; set; }
    public decimal NetSoldValue { get; set; }
    public decimal GrossSoldValue { get; set; }

    public decimal Profit { get; set; }
    public decimal TotalStockQty { get; set; }

    public decimal QtyPercent => QuantityPurchased != 0
        ? Math.Round(QuantitySold / QuantityPurchased * 100, 2)
        : (QuantitySold != 0 ? 100 : 0);

    public decimal ValPercent => NetPurchasedValue != 0
        ? Math.Round(NetSoldValue / NetPurchasedValue * 100, 2)
        : (NetSoldValue != 0 ? 100 : 0);
}
