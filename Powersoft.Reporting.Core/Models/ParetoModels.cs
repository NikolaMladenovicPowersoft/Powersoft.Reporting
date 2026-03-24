namespace Powersoft.Reporting.Core.Models;

public class ParetoRow
{
    public int Rank { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
    public decimal CumulativeValue { get; set; }
    public decimal Percentage { get; set; }
    public decimal CumulativePercentage { get; set; }
    public string Classification { get; set; } = "";
}

public class ParetoFilter
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public ParetoDimension Dimension { get; set; } = ParetoDimension.Item;
    public ParetoMetric Metric { get; set; } = ParetoMetric.Value;
    public bool IncludeVat { get; set; }
    public List<string>? StoreCodes { get; set; }
    public decimal ClassAThreshold { get; set; } = 80;
    public decimal ClassBThreshold { get; set; } = 95;

    public ItemsSelectionFilter? ItemsSelection { get; set; }
}

public class ParetoResult
{
    public List<ParetoRow> Rows { get; set; } = new();
    public decimal GrandTotal { get; set; }
    public int ClassACount { get; set; }
    public int ClassBCount { get; set; }
    public int ClassCCount { get; set; }
    public decimal ClassAValue { get; set; }
    public decimal ClassBValue { get; set; }
    public decimal ClassCValue { get; set; }
}

public enum ParetoDimension
{
    Item,
    Customer,
    Category,
    Supplier,
    Brand
}

public enum ParetoMetric
{
    Value,
    Quantity
}
