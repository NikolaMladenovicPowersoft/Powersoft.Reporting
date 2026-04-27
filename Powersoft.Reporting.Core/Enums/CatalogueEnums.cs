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
    InvoiceNo = 26,
    [Display(Name = "Item Agent")]
    ItemAgent = 27,
    [Display(Name = "Customer/Supplier Agent")]
    CSAgent = 28,
    [Display(Name = "VAT Code")]
    VAT = 29,
    [Display(Name = "Customer Category 1")]
    CustomerCategory1 = 30,
    [Display(Name = "Customer Category 2")]
    CustomerCategory2 = 31,
    [Display(Name = "Customer Category 3")]
    CustomerCategory3 = 32,
    Town = 33,
    [Display(Name = "Recommended By")]
    RecommendedBy = 34
}

/// <summary>
/// Controls which cost/price basis is used for profit and stock value calculations.
/// IDs match the original Powersoft365 PriceID values.
/// </summary>
public enum CatalogueCostBasis
{
    [Display(Name = "Default Price")]
    DefaultPrice = 0,
    [Display(Name = "Price 1")]
    Price1 = 1,
    [Display(Name = "Price 2")]
    Price2 = 2,
    [Display(Name = "Price 3")]
    Price3 = 3,
    [Display(Name = "Price 4")]
    Price4 = 4,
    [Display(Name = "Price 5")]
    Price5 = 5,
    [Display(Name = "Price 6")]
    Price6 = 6,
    [Display(Name = "Price 7")]
    Price7 = 7,
    [Display(Name = "Price 8")]
    Price8 = 8,
    [Display(Name = "Price 9")]
    Price9 = 9,
    [Display(Name = "Price 10")]
    Price10 = 10,
    [Display(Name = "Weighted Average Cost")]
    WeightedAverageCost = 87,
    [Display(Name = "Average Cost")]
    AverageCost = 88,
    [Display(Name = "Cost on Sale")]
    CostOnSale = 98,
    [Display(Name = "Latest Cost")]
    LatestCost = 99
}

/// <summary>
/// Controls which date column (on tbl_*Header rows) drives the period filter.
/// Mirrors original rbDateSelection radio in repPowerReportCatalogue.aspx.vb.
/// </summary>
public enum CatalogueDateBasis
{
    [Display(Name = "Transaction Date")]
    TransactionDate = 0,
    [Display(Name = "Session Date")]
    SessionDate = 1
}

