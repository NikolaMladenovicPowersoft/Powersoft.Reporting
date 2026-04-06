namespace Powersoft.Reporting.Core.Models;

public class PurchasesSalesMonthlyRow
{
    public string? Level1 { get; set; }
    public string? Level1Value { get; set; }
    public string? Level2 { get; set; }
    public string? Level2Value { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public int TransYear { get; set; }

    public decimal[] Purchased { get; set; } = new decimal[12];
    public decimal[] Sold { get; set; } = new decimal[12];

    public decimal TotalPurchased => Purchased.Sum();
    public decimal TotalSold => Sold.Sum();
}
