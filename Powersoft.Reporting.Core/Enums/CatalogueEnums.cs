using System.ComponentModel.DataAnnotations;

namespace Powersoft.Reporting.Core.Enums;

public enum CatalogueReportMode
{
    Detailed = 0,
    Summary = 1
}

public enum CatalogueReportOn
{
    Sale = 0,
    Purchase = 1,
    Both = 2
}

public enum CatalogueGroupBy
{
    None = 0,
    Store = 1,
    Category = 2,
    Department = 3,
    Supplier = 4,
    Brand = 5,
    Season = 6,
    Model = 7,
    Colour = 8,
    Size = 9,
    Customer = 10,
    Agent = 11,
    [Display(Name = "Payment Type")]
    PaymentType = 12,
    [Display(Name = "Transaction Date")]
    TransactionDate = 13,
    [Display(Name = "Transaction Month")]
    TransactionMonth = 14,
    [Display(Name = "Invoice Type")]
    InvoiceType = 15,
    Station = 16,
    Franchise = 17,
    [Display(Name = "Attribute 1")]
    ItemAttr1 = 18,
    [Display(Name = "Attribute 2")]
    ItemAttr2 = 19,
    [Display(Name = "Attribute 3")]
    ItemAttr3 = 20,
    [Display(Name = "Attribute 4")]
    ItemAttr4 = 21,
    [Display(Name = "Attribute 5")]
    ItemAttr5 = 22,
    [Display(Name = "Attribute 6")]
    ItemAttr6 = 23,
    User = 24,
    [Display(Name = "Z Report")]
    ZReport = 25,
    [Display(Name = "Invoice No")]
    InvoiceNo = 26
}

/// <summary>
/// Controls which cost/price basis is used for profit and stock value calculations.
/// IDs match the original Powersoft365 PriceID values.
/// </summary>
public enum CatalogueCostBasis
{
    [Display(Name = "Latest Cost")]
    LatestCost = 99,
    [Display(Name = "Average Cost")]
    AverageCost = 88,
    [Display(Name = "Weighted Average Cost")]
    WeightedAverageCost = 87,
    [Display(Name = "Cost on Sale")]
    CostOnSale = 98,
    [Display(Name = "Price 1")]
    Price1 = 1,
    [Display(Name = "Price 2")]
    Price2 = 2,
    [Display(Name = "Price 3")]
    Price3 = 3
}
