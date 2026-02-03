namespace Powersoft.Reporting.Core.Models;

public class AverageBasketRow
{
    public string Period { get; set; } = string.Empty;
    public string? Level1 { get; set; }
    public string? Level1Value { get; set; }
    public string? Level2 { get; set; }
    public string? Level2Value { get; set; }
    
    // Current Year
    public int CYInvoiceCount { get; set; }
    public int CYCreditCount { get; set; }
    public decimal CYQtySold { get; set; }
    public decimal CYQtyReturned { get; set; }
    public decimal CYNetSales { get; set; }
    public decimal CYNetReturns { get; set; }
    public decimal CYVatSales { get; set; }
    public decimal CYVatReturns { get; set; }
    public decimal CYGrossSales { get; set; }
    public decimal CYGrossReturns { get; set; }
    
    // Computed Current Year
    public int CYTotalTransactions => CYInvoiceCount - CYCreditCount;
    public decimal CYTotalQty => CYQtySold - CYQtyReturned;
    public decimal CYTotalNet => CYNetSales - CYNetReturns;
    public decimal CYTotalGross => CYGrossSales - CYGrossReturns;
    public decimal CYAverageQty => CYTotalTransactions > 0 ? CYTotalQty / CYTotalTransactions : 0;
    public decimal CYAverageNet => CYTotalTransactions > 0 ? CYTotalNet / CYTotalTransactions : 0;
    public decimal CYAverageGross => CYTotalTransactions > 0 ? CYTotalGross / CYTotalTransactions : 0;
    
    // Last Year (for comparison)
    public int LYInvoiceCount { get; set; }
    public int LYCreditCount { get; set; }
    public decimal LYNetSales { get; set; }
    public decimal LYNetReturns { get; set; }
    public decimal LYVatSales { get; set; }
    public decimal LYVatReturns { get; set; }
    public decimal LYTotalNet { get; set; }
    public decimal LYTotalGross { get; set; }
    
    // Computed Last Year
    public int LYTotalTransactions => LYInvoiceCount - LYCreditCount;
    public decimal LYAverageNet => LYTotalTransactions > 0 ? LYTotalNet / LYTotalTransactions : 0;
    public decimal LYAverageGross => LYTotalTransactions > 0 ? LYTotalGross / LYTotalTransactions : 0;
    
    // Year over Year
    public decimal YoYChangePercent => LYTotalNet != 0 
        ? Math.Round((CYTotalNet - LYTotalNet) / LYTotalNet * 100, 2) 
        : (CYTotalNet > 0 ? 100 : 0);
}
