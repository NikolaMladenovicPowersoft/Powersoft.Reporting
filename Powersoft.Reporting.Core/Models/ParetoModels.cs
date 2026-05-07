namespace Powersoft.Reporting.Core.Models;

public class ParetoRow
{
    public int Rank { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
    public decimal Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Profit { get; set; }
    public decimal CumulativeValue { get; set; }
    public decimal Percentage { get; set; }
    public decimal CumulativePercentage { get; set; }
    public string Classification { get; set; } = "";
    public bool IsDisplay { get; set; }
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
    public bool ExcludeNegativeAmounts { get; set; } = true;
    public bool ShowOthers { get; set; }
    public ParetoProfitBasis ProfitBasis { get; set; } = ParetoProfitBasis.LatestCost;
    public int DefaultPriceIndex { get; set; }

    public ItemsSelectionFilter? ItemsSelection { get; set; }
    public int TimezoneOffsetMinutes { get; set; }

    public decimal PriceInterval { get; set; }
    public int PriceOnIndex { get; set; }
    public bool PriceOnIncludesVat { get; set; }

    public List<string>? CustomerCodes { get; set; }
    public List<int>? CustomerCategory1Ids { get; set; }
    public List<int>? CustomerCategory2Ids { get; set; }
}

public class ParetoResult
{
    public List<ParetoRow> Rows { get; set; } = new();
    public decimal GrandTotal { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalSubtotal { get; set; }
    public decimal TotalProfit { get; set; }
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
    CustomerCategory1,
    CustomerCategory2,
    Category,
    Department,
    Brand,
    Season,
    Supplier,
    Model,
    Colour,
    Size,
    GroupSize,
    Fabric,
    Store,
    User,
    ByPrice
}

public enum ParetoMetric
{
    Value,
    Quantity,
    Profit
}

public enum ParetoProfitBasis
{
    DefaultPrice = 0,
    Price1 = 1,
    Price2 = 2,
    Price3 = 3,
    Price4 = 4,
    Price5 = 5,
    Price6 = 6,
    Price7 = 7,
    Price8 = 8,
    Price9 = 9,
    Price10 = 10,
    WeightedAverageCost = 87,
    AverageCost = 88,
    LatestCost = 99
}
