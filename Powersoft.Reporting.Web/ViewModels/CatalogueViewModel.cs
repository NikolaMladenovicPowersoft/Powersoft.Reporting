using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class CatalogueViewModel
{
    [Required] [DataType(DataType.Date)] [Display(Name = "Date From")]
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    [Required] [DataType(DataType.Date)] [Display(Name = "Date To")]
    public DateTime DateTo { get; set; } = DateTime.Today;

    [Display(Name = "Date Basis")]
    public CatalogueDateBasis DateBasis { get; set; } = CatalogueDateBasis.TransactionDate;

    [Display(Name = "Use Time")]
    public bool UseDateTime { get; set; } = false;

    [Display(Name = "Report Mode")]
    public CatalogueReportMode ReportMode { get; set; } = CatalogueReportMode.Detailed;

    [Display(Name = "Report On")]
    public CatalogueReportOn ReportOn { get; set; } = CatalogueReportOn.Sale;

    [Display(Name = "Group By")]
    public CatalogueGroupBy PrimaryGroup { get; set; } = CatalogueGroupBy.None;

    [Display(Name = "Then By")]
    public CatalogueGroupBy SecondaryGroup { get; set; } = CatalogueGroupBy.None;

    [Display(Name = "Third Group")]
    public CatalogueGroupBy ThirdGroup { get; set; } = CatalogueGroupBy.None;

    // --- Cost / Profit ---
    [Display(Name = "Profit Based On")]
    public CatalogueCostBasis ProfitBasedOn { get; set; } = CatalogueCostBasis.LatestCost;

    [Display(Name = "Includes VAT")]
    public bool ProfitIncludesVat { get; set; }

    [Display(Name = "Stock Value Based On")]
    public CatalogueCostBasis StockValueBasedOn { get; set; } = CatalogueCostBasis.LatestCost;

    [Display(Name = "Includes VAT")]
    public bool StockValueIncludesVat { get; set; }

    [Display(Name = "Cost Type")]
    public CatalogueCostBasis CostType { get; set; } = CatalogueCostBasis.CostOnSale;

    // --- Display value columns (comma-separated string for form binding) ---
    public string DisplayColumnsString { get; set; } = "ItemCode,ItemName,Quantity,Value,Discount,NetValue,VatAmount,GrossAmount";

    // --- Column order (comma-separated SqlCol list) ---
    // Empty = default server-side order. Persisted as part of the layout so a user's custom
    // column order survives logout and is shared with public/named layouts.
    // Only affects client-side DOM reorder (see Catalogue.cshtml _initColumnReorder).
    public string? ColumnOrder { get; set; }

    // --- Quick toggles (synced bidirectionally with Display Columns chips) ---
    // ShowProfit  ↔ {Profit, Markup, Margin}
    // ShowStock   ↔ {TotalStockQty, TotalStockValue}
    [Display(Name = "Show Profit")]
    public bool ShowProfit { get; set; } = true;

    [Display(Name = "Show Stock")]
    public bool ShowStock { get; set; }

    public string? DatePreset { get; set; }
    public string ReportTitle { get; set; } = "Power Report";
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

    public string? ItemsSelectionJson { get; set; }
    public List<Store> AvailableStores { get; set; } = new();

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public List<CatalogueRow> Results { get; set; } = new();
    public CatalogueTotals? Totals { get; set; }

    public string? ConnectedDatabase { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanSchedule { get; set; } = true;
    public bool HasSavedLayout { get; set; }
    public string? HiddenColumns { get; set; }

    public bool HasPrimaryGroup => PrimaryGroup != CatalogueGroupBy.None;
    public bool HasSecondaryGroup => SecondaryGroup != CatalogueGroupBy.None;
    public bool HasThirdGroup => ThirdGroup != CatalogueGroupBy.None;
    public bool HasAnyGroup => HasPrimaryGroup || HasSecondaryGroup || HasThirdGroup;
    public bool IsSummary => ReportMode == CatalogueReportMode.Summary;

    public List<string> GetDisplayColumns() =>
        (DisplayColumnsString ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    public bool HasDisplayColumn(string col) =>
        GetDisplayColumns().Contains(col, StringComparer.OrdinalIgnoreCase);

    public CatalogueFilter ToCatalogueFilter()
    {
        return new CatalogueFilter
        {
            DateFrom = DateFrom,
            DateTo = DateTo,
            DateBasis = DateBasis,
            UseDateTime = UseDateTime,
            ReportMode = ReportMode,
            ReportOn = ReportOn,
            PrimaryGroup = PrimaryGroup,
            SecondaryGroup = SecondaryGroup,
            ThirdGroup = ThirdGroup,
            ProfitBasedOn = ProfitBasedOn,
            ProfitIncludesVat = ProfitIncludesVat,
            StockValueBasedOn = StockValueBasedOn,
            StockValueIncludesVat = StockValueIncludesVat,
            CostType = CostType,
            DisplayColumns = GetDisplayColumns(),
            ShowProfit = ShowProfit,
            ShowStock = ShowStock,
            StoreCodes = SelectedStoreCodes,
            PageNumber = PageNumber,
            PageSize = PageSize,
            SortColumn = SortColumn,
            SortDirection = SortDirection,
            FilterValues = FilterValues ?? new(),
            FilterOperators = FilterOperators ?? new()
        };
    }
}
