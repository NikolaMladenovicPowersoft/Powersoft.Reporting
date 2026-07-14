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

    [Display(Name = "Show On Order")]
    public bool ShowOnOrder { get; set; }

    [Display(Name = "Show Reservation")]
    public bool ShowReservation { get; set; }

    [Display(Name = "Show Available")]
    public bool ShowAvailable { get; set; }

    [Display(Name = "Include Additional Charges")]
    public bool IncludeAdditionalCharges { get; set; } = true;

    [Display(Name = "Sort Sizes by Sequence")]
    public bool SortBySizeSequence { get; set; }

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

    public string? ItemsSelectionJson { get; set; }

    public List<Store> AvailableStores { get; set; } = new();

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public List<PurchasesSalesRow> Results { get; set; } = new();
    public List<PurchasesSalesMonthlyRow> MonthlyResults { get; set; } = new();
    public PurchasesSalesTotals? Totals { get; set; }

    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanSchedule { get; set; } = true;
    public bool HasSavedLayout { get; set; }
    public string? HiddenColumns { get; set; }

    // Fashion dimension availability (data-driven): show the Model/Colour/Size/GroupSize/
    // Fabric/Attribute filters only when the tenant's items actually use them.
    public bool ShowModels { get; set; }
    public bool ShowColours { get; set; }
    public bool ShowSizes { get; set; }
    public bool ShowGroupSizes { get; set; }
    public bool ShowFabrics { get; set; }
    public bool ShowAttributes { get; set; }

    public bool HasPrimaryGroup => PrimaryGroup != PsGroupBy.None;
    public bool HasSecondaryGroup => SecondaryGroup != PsGroupBy.None;
    public bool HasThirdGroup => ThirdGroup != PsGroupBy.None;
    public bool HasAnyGroup => HasPrimaryGroup || HasSecondaryGroup || HasThirdGroup;

    public decimal TotalPurchasedQty => Totals?.TotalQtyPurchased ?? Results.Sum(r => r.QuantityPurchased);
    public decimal TotalPurchasedNet => Totals?.TotalNetPurchased ?? Results.Sum(r => r.NetPurchasedValue);
    public decimal TotalPurchasedGross => Totals?.TotalGrossPurchased ?? Results.Sum(r => r.GrossPurchasedValue);
    public decimal TotalSoldQty => Totals?.TotalQtySold ?? Results.Sum(r => r.QuantitySold);
    public decimal TotalSoldNet => Totals?.TotalNetSold ?? Results.Sum(r => r.NetSoldValue);
    public decimal TotalSoldGross => Totals?.TotalGrossSold ?? Results.Sum(r => r.GrossSoldValue);
    public decimal TotalProfit => Totals != null ? Totals.TotalProfit : Results.Sum(r => r.Profit);
    public decimal TotalOnOrder => Totals?.TotalQtyOnOrder ?? Results.Sum(r => r.QtyOnOrder);
    public decimal TotalReserved => Totals?.TotalQtyReserved ?? Results.Sum(r => r.QtyReserved);
    public decimal TotalAvailable => Totals?.TotalQtyAvailable ?? Results.Sum(r => r.QtyAvailable);

    public decimal MonthlyTotalPurchased => MonthlyResults.Sum(r => r.TotalPurchased);
    public decimal MonthlyTotalSold => MonthlyResults.Sum(r => r.TotalSold);

    public decimal TotalPurchasedValue => IncludeVat ? TotalPurchasedGross : TotalPurchasedNet;
    public decimal TotalSoldValue => IncludeVat ? TotalSoldGross : TotalSoldNet;

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
            ShowOnOrder = ShowOnOrder,
            ShowReservation = ShowReservation,
            ShowAvailable = ShowAvailable,
            IncludeAdditionalCharges = IncludeAdditionalCharges,
            SortBySizeSequence = SortBySizeSequence,
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
