namespace Powersoft.Reporting.Core.Models;

public class ChartDataPoint
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal? CompareValue { get; set; }
}

public class ChartFilter
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
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

public enum ChartDimension
{
    Category,
    Store,
    Brand,
    Customer,
    Item,
    Supplier,
    Department
}

public enum ChartMetric
{
    Value,
    Quantity
}
