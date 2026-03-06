using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class PurchasesSalesViewModel
{
    [Required] [DataType(DataType.Date)] [Display(Name = "Date From")]
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    [Required] [DataType(DataType.Date)] [Display(Name = "Date To")]
    public DateTime DateTo { get; set; } = DateTime.Today;

    [Display(Name = "Report Mode")]
    public PsReportMode ReportMode { get; set; } = PsReportMode.Detailed;

    [Display(Name = "Group By")]
    public PsGroupBy PrimaryGroup { get; set; } = PsGroupBy.None;

    [Display(Name = "Then By")]
    public PsGroupBy SecondaryGroup { get; set; } = PsGroupBy.None;

    [Display(Name = "Third Group")]
    public PsGroupBy ThirdGroup { get; set; } = PsGroupBy.None;

    [Display(Name = "Include VAT")]
    public bool IncludeVat { get; set; }

    [Display(Name = "Show Profit")]
    public bool ShowProfit { get; set; } = true;

    [Display(Name = "Show Stock")]
    public bool ShowStock { get; set; }

    public string? DatePreset { get; set; }
    public string SortColumn { get; set; } = "ItemCode";
    public string SortDirection { get; set; } = "ASC";

    public Dictionary<string, string> FilterValues { get; set; } = new();
    public Dictionary<string, string> FilterOperators { get; set; } = new();

    public List<string> SelectedStoreCodes { get; set; } = new();
    public string SelectedStoreCodesString
    {
        get => string.Join(",", SelectedStoreCodes);
        set => SelectedStoreCodes = string.IsNullOrEmpty(value)
            ? new() : value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public List<int> SelectedItemIds { get; set; } = new();
    public string SelectedItemIdsString
    {
        get => string.Join(",", SelectedItemIds);
        set => SelectedItemIds = string.IsNullOrEmpty(value)
            ? new() : value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out _)).Select(s => int.Parse(s.Trim())).ToList();
    }

    public List<Store> AvailableStores { get; set; } = new();

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public List<PurchasesSalesRow> Results { get; set; } = new();
    public PurchasesSalesTotals? Totals { get; set; }

    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanSchedule { get; set; } = true;
    public bool HasSavedLayout { get; set; }
    public string? HiddenColumns { get; set; }

    public bool HasPrimaryGroup => PrimaryGroup != PsGroupBy.None;
    public bool HasSecondaryGroup => SecondaryGroup != PsGroupBy.None;
    public bool HasThirdGroup => ThirdGroup != PsGroupBy.None;
    public bool HasAnyGroup => HasPrimaryGroup || HasSecondaryGroup || HasThirdGroup;

    public decimal TotalPurchasedQty => Results.Sum(r => r.QuantityPurchased);
    public decimal TotalPurchasedNet => Results.Sum(r => r.NetPurchasedValue);
    public decimal TotalSoldQty => Results.Sum(r => r.QuantitySold);
    public decimal TotalSoldNet => Results.Sum(r => r.NetSoldValue);
    public decimal TotalProfit => Results.Sum(r => r.Profit);

    public PurchasesSalesFilter ToPurchasesSalesFilter()
    {
        return new PurchasesSalesFilter
        {
            DateFrom = DateFrom,
            DateTo = DateTo,
            ReportMode = ReportMode,
            PrimaryGroup = PrimaryGroup,
            SecondaryGroup = SecondaryGroup,
            ThirdGroup = ThirdGroup,
            IncludeVat = IncludeVat,
            ShowProfit = ShowProfit,
            ShowStock = ShowStock,
            StoreCodes = SelectedStoreCodes,
            ItemIds = SelectedItemIds,
            PageNumber = PageNumber,
            PageSize = PageSize,
            SortColumn = SortColumn,
            SortDirection = SortDirection,
            FilterValues = FilterValues ?? new(),
            FilterOperators = FilterOperators ?? new()
        };
    }
}
