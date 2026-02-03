using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class AverageBasketViewModel
{
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    public bool IncludeVat { get; set; } = false;
    public bool CompareLastYear { get; set; } = false;
    
    // Date preset for quick selection
    public string? DatePreset { get; set; }
    
    public List<AverageBasketRow> Results { get; set; } = new();
    
    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Check if results are grouped
    public bool HasGrouping => GroupBy != GroupByType.None;
    
    // Totals
    public int TotalTransactions => Results.Sum(r => r.CYTotalTransactions);
    public decimal TotalQty => Results.Sum(r => r.CYTotalQty);
    public decimal TotalNetSales => Results.Sum(r => r.CYTotalNet);
    public decimal TotalGrossSales => Results.Sum(r => r.CYTotalGross);
    public decimal OverallAverageBasket => TotalTransactions > 0 
        ? (IncludeVat ? TotalGrossSales : TotalNetSales) / TotalTransactions 
        : 0;
    
    // Last Year totals (when CompareLastYear is true)
    public int TotalLYTransactions => Results.Sum(r => r.LYTotalTransactions);
    public decimal TotalLYNetSales => Results.Sum(r => r.LYTotalNet);
    public decimal TotalLYGrossSales => Results.Sum(r => r.LYTotalGross);
    public decimal OverallYoYChangePercent => TotalLYNetSales != 0 
        ? Math.Round((TotalNetSales - TotalLYNetSales) / TotalLYNetSales * 100, 2) 
        : (TotalNetSales > 0 ? 100 : 0);
}
