namespace Powersoft.Reporting.Core.Models;

public class ChartDataPoint
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal? CompareValue { get; set; }
    public decimal? Value2 { get; set; }
    public decimal? CompareValue2 { get; set; }
    public decimal? DiffValue { get; set; }
}

public class ChartFilter
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public ChartMode Mode { get; set; } = ChartMode.Sales;
    public ChartDimension Dimension { get; set; } = ChartDimension.Category;
    public ChartMetric Metric { get; set; } = ChartMetric.Value;
    public int TopN { get; set; } = 10;
    public bool ShowOthers { get; set; } = true;
    public bool CompareLastYear { get; set; }
    public bool IncludeVat { get; set; }
    public List<string>? StoreCodes { get; set; }
    public string ChartType { get; set; } = "pie";

    public ItemsSelectionFilter? ItemsSelection { get; set; }
}

public enum ChartMode
{
    Sales,
    SalesVsReturns,
    Purchases,
    PurchasesVsReturns,
    SalesVsPurchases
}

public enum ChartDimension
{
    Category,
    Store,
    Brand,
    Customer,
    Item,
    Supplier,
    Department,
    Season,
    Agent,
    User,
    CSAgent,
    Model,
    Colour,
    Size,
    SizeGroup,
    Fabric,
    Attr1,
    Attr2,
    Attr3,
    Attr4,
    Attr5,
    Attr6,
    CustCat1,
    CustCat2,
    CustCat3,
    HourOfDay
}

public enum ChartMetric
{
    Value,
    Quantity,
    Count
}
