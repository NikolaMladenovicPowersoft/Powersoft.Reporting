namespace Powersoft.Reporting.Core.Models;

public class BelowMinStockRow
{
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string StoreCode { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string? CategoryName { get; set; }
    public string? DepartmentName { get; set; }
    public string? BrandName { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal Difference { get; set; }
    public decimal? Cost { get; set; }
    public decimal? StockValue { get; set; }
    public string? Shelf { get; set; }
}

public class BelowMinStockFilter
{
    public List<string>? StoreCodes { get; set; }
    public ItemsSelectionFilter? ItemsSelection { get; set; }
    public string SortColumn { get; set; } = "ItemCode";
    public string SortDirection { get; set; } = "ASC";
}
