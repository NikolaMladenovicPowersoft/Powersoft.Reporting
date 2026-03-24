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
    public bool ShowStores { get; set; } = true;
    public string StoresJson { get; set; } = "[]";
}
