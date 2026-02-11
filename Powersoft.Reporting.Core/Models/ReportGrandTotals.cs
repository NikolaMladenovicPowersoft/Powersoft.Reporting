namespace Powersoft.Reporting.Core.Models;

public class ReportGrandTotals
{
    public int TotalInvoices { get; set; }
    public int TotalCredits { get; set; }
    public int NetTransactions => TotalInvoices - TotalCredits;
    public decimal TotalQtySold { get; set; }
    public decimal TotalQtyReturned { get; set; }
    public decimal NetQty => TotalQtySold - TotalQtyReturned;
    public decimal TotalNetSales { get; set; }
    public decimal TotalNetReturns { get; set; }
    public decimal TotalVatSales { get; set; }
    public decimal TotalVatReturns { get; set; }
    public decimal NetSales => TotalNetSales - TotalNetReturns;
    public decimal GrossSales => NetSales + (TotalVatSales - TotalVatReturns);
    public decimal AverageBasketNet => NetTransactions > 0 ? NetSales / NetTransactions : 0;
    public decimal AverageBasketGross => NetTransactions > 0 ? GrossSales / NetTransactions : 0;
    public decimal AverageQty => NetTransactions > 0 ? NetQty / NetTransactions : 0;

    // Last Year totals
    public int LYTotalInvoices { get; set; }
    public int LYTotalCredits { get; set; }
    public int LYNetTransactions => LYTotalInvoices - LYTotalCredits;
    public decimal LYNetSales { get; set; }
    public decimal LYNetReturns { get; set; }
    public decimal LYVatSales { get; set; }
    public decimal LYVatReturns { get; set; }
    public decimal LYTotalNet => LYNetSales - LYNetReturns;
    public decimal LYTotalGross => LYTotalNet + (LYVatSales - LYVatReturns);
    public decimal LYAverageBasketNet => LYNetTransactions > 0 ? LYTotalNet / LYNetTransactions : 0;
    public decimal LYAverageBasketGross => LYNetTransactions > 0 ? LYTotalGross / LYNetTransactions : 0;

    public decimal YoYChangePercent => LYTotalNet != 0
        ? Math.Round((NetSales - LYTotalNet) / Math.Abs(LYTotalNet) * 100, 2)
        : (NetSales > 0 ? 100 : 0);
}
