namespace Powersoft.Reporting.Core.Models;

public class DimensionItem
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public enum FilterMode
{
    All = 0,
    Include = 1,
    Exclude = 2
}

public class DimensionFilter
{
    public List<string> Ids { get; set; } = new();
    public FilterMode Mode { get; set; } = FilterMode.All;

    public bool HasFilter => Mode != FilterMode.All && Ids.Count > 0;
}

public enum StockFilter { All, WithStock, WithoutStock }

public class ItemsSelectionFilter
{
    public DimensionFilter Categories { get; set; } = new();
    public DimensionFilter Departments { get; set; } = new();
    public DimensionFilter Brands { get; set; } = new();
    public DimensionFilter Seasons { get; set; } = new();
    public DimensionFilter Suppliers { get; set; } = new();
    public DimensionFilter Customers { get; set; } = new();
    public DimensionFilter Stores { get; set; } = new();
    public DimensionFilter Items { get; set; } = new();

    // Fashion/retail dimensions (tbl_Item / tbl_Model columns).
    // Mirrors legacy ItemsSelections widget: ShowModels, ShowColours, ShowSizes, ShowGroupSizes, ShowFabrics.
    public DimensionFilter Models { get; set; } = new();
    public DimensionFilter Colours { get; set; } = new();
    public DimensionFilter Sizes { get; set; } = new();
    public DimensionFilter GroupSizes { get; set; } = new();
    public DimensionFilter Fabrics { get; set; } = new();

    // Item attribute dimensions (tbl_Item.fk_AttrID1..6).
    public DimensionFilter Attributes1 { get; set; } = new();
    public DimensionFilter Attributes2 { get; set; } = new();
    public DimensionFilter Attributes3 { get; set; } = new();
    public DimensionFilter Attributes4 { get; set; } = new();
    public DimensionFilter Attributes5 { get; set; } = new();
    public DimensionFilter Attributes6 { get; set; } = new();

    // Sale-leg only (tbl_InvoiceHeader / tbl_CreditNoteHeader): applied only when leg is a sales leg.
    public DimensionFilter Agents { get; set; } = new();
    public DimensionFilter PostalCodes { get; set; } = new();

    // Sale-leg only (header columns present only on invoice/credit headers).
    // Mirrors legacy repPowerReportCatalogue chkPaymentType/chkZReport/chkTown filters.
    public DimensionFilter PaymentTypes { get; set; } = new();
    public DimensionFilter ZReports { get; set; } = new();
    public DimensionFilter Towns { get; set; } = new();

    // Both legs — h.fk_UserCode exists on every header table.
    public DimensionFilter Users { get; set; } = new();

    // Item-level property filters (tbl_Item columns)
    public StockFilter Stock { get; set; } = StockFilter.All;
    public bool? ECommerceOnly { get; set; }
    public DateTime? ModifiedAfter { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? ReleasedAfter { get; set; }

    public bool HasPropertyFilters =>
        Stock != StockFilter.All || ECommerceOnly.HasValue
        || ModifiedAfter.HasValue || CreatedAfter.HasValue || ReleasedAfter.HasValue;
}

public class ItemsSelectionConfig
{
    public bool ShowCategories { get; set; } = true;
    public bool ShowDepartments { get; set; } = true;
    public bool ShowBrands { get; set; } = true;
    public bool ShowSeasons { get; set; } = true;
    public bool ShowSuppliers { get; set; } = true;
    public bool ShowItems { get; set; }
    public bool ShowCustomers { get; set; }
    public bool ShowAgents { get; set; }
    public bool ShowPostalCodes { get; set; }
    public bool ShowStores { get; set; } = true;
    public bool ShowPaymentTypes { get; set; }
    public bool ShowZReports { get; set; }
    public bool ShowTowns { get; set; }
    public bool ShowUsers { get; set; }
    public bool ShowModels { get; set; }
    public bool ShowColours { get; set; }
    public bool ShowSizes { get; set; }
    public bool ShowGroupSizes { get; set; }
    public bool ShowFabrics { get; set; }
    public bool ShowAttributes { get; set; }
    public bool ShowStockFilter { get; set; } = true;
    public bool ShowECommerce { get; set; } = true;
    public bool ShowModifiedDate { get; set; } = true;
    public string StoresJson { get; set; } = "[]";
    public string SavedFilterJson { get; set; } = "";
}
