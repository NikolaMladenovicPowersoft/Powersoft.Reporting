using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class AverageBasketViewModel
{
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public bool IncludeVat { get; set; } = false;
    
    public List<AverageBasketRow> Results { get; set; } = new();
    
    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Totals
    public int TotalTransactions => Results.Sum(r => r.CYTotalTransactions);
    public decimal TotalQty => Results.Sum(r => r.CYTotalQty);
    public decimal TotalNetSales => Results.Sum(r => r.CYTotalNet);
    public decimal TotalGrossSales => Results.Sum(r => r.CYTotalGross);
    public decimal OverallAverageBasket => TotalTransactions > 0 
        ? (IncludeVat ? TotalGrossSales : TotalNetSales) / TotalTransactions 
        : 0;
}
