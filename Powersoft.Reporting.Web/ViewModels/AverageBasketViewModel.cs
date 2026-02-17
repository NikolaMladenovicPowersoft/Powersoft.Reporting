using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class AverageBasketViewModel
{
    [Required(ErrorMessage = "Date From is required")]
    [DataType(DataType.Date)]
    [Display(Name = "Date From")]
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    
    [Required(ErrorMessage = "Date To is required")]
    [DataType(DataType.Date)]
    [Display(Name = "Date To")]
    public DateTime DateTo { get; set; } = DateTime.Today;
    
    [Display(Name = "Breakdown")]
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    
    [Display(Name = "Group By")]
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    
    [Display(Name = "Then By")]
    public GroupByType SecondaryGroupBy { get; set; } = GroupByType.None;
    
    [Display(Name = "Include VAT")]
    public bool IncludeVat { get; set; } = false;
    
    [Display(Name = "Compare with Last Year")]
    public bool CompareLastYear { get; set; } = false;
    
    public string? DatePreset { get; set; }
    
    public string SortColumn { get; set; } = "Period";
    public string SortDirection { get; set; } = "ASC";
    
    public Dictionary<string, string> FilterValues { get; set; } = new();
    public Dictionary<string, string> FilterOperators { get; set; } = new();
    
    public List<string> SelectedStoreCodes { get; set; } = new();
    public string SelectedStoreCodesString 
    { 
        get => string.Join(",", SelectedStoreCodes);
        set => SelectedStoreCodes = string.IsNullOrEmpty(value) 
            ? new List<string>() 
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
    
    public List<int> SelectedItemIds { get; set; } = new();
    public string SelectedItemIdsString 
    { 
        get => string.Join(",", SelectedItemIds);
        set => SelectedItemIds = string.IsNullOrEmpty(value) 
            ? new List<int>() 
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out _))
                .Select(s => int.Parse(s.Trim())).ToList();
    }
    
    public List<Store> AvailableStores { get; set; } = new();
    
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    
    public List<AverageBasketRow> Results { get; set; } = new();
    public ReportGrandTotals? GrandTotals { get; set; }
    
    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    
    public bool HasGrouping => GroupBy != GroupByType.None;
    public bool HasSecondaryGrouping => SecondaryGroupBy != GroupByType.None;
    public bool HasStoreFilter => SelectedStoreCodes.Any();
    public bool HasItemFilter => SelectedItemIds.Any();
    
    public int TotalTransactions => Results.Sum(r => r.CYTotalTransactions);
    public decimal TotalQty => Results.Sum(r => r.CYTotalQty);
    public decimal TotalNetSales => Results.Sum(r => r.CYTotalNet);
    public decimal TotalGrossSales => Results.Sum(r => r.CYTotalGross);
    public decimal OverallAverageBasket => TotalTransactions > 0 
        ? (IncludeVat ? TotalGrossSales : TotalNetSales) / TotalTransactions 
        : 0;
    
    public int TotalLYTransactions => Results.Sum(r => r.LYTotalTransactions);
    public decimal TotalLYNetSales => Results.Sum(r => r.LYTotalNet);
    public decimal TotalLYGrossSales => Results.Sum(r => r.LYTotalGross);
    public decimal OverallYoYChangePercent => TotalLYNetSales != 0 
        ? Math.Round((TotalNetSales - TotalLYNetSales) / TotalLYNetSales * 100, 2) 
        : (TotalNetSales > 0 ? 100 : 0);
    
    public ReportFilter ToReportFilter()
    {
        return new ReportFilter
        {
            DateFrom = DateFrom,
            DateTo = DateTo,
            Breakdown = Breakdown,
            GroupBy = GroupBy,
            SecondaryGroupBy = SecondaryGroupBy,
            IncludeVat = IncludeVat,
            CompareLastYear = CompareLastYear,
            StoreCodes = SelectedStoreCodes,
            ItemIds = SelectedItemIds,
            PageNumber = PageNumber,
            PageSize = PageSize,
            DatePreset = DatePreset,
            SortColumn = SortColumn,
            SortDirection = SortDirection,
            FilterValues = FilterValues ?? new(),
            FilterOperators = FilterOperators ?? new()
        };
    }
}
