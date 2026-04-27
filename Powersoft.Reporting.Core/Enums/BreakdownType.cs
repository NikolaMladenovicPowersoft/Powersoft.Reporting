using System.ComponentModel.DataAnnotations;

namespace Powersoft.Reporting.Core.Enums;

public enum BreakdownType
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

public enum ReportLayout
{
    [Display(Name = "Average Basket")]
    AverageBasket = 0,
    [Display(Name = "People Count")]
    PeopleCount = 1
}

public enum GroupByType
{
    None,
    Store,
    Category,
    Department,
    Brand,
    Season,
    Customer,
    User,
    Supplier,
    Model,
    Colour,
    Size,
    [Display(Name = "Customer Category 1")]
    CustomerCategory1,
    [Display(Name = "Customer Category 2")]
    CustomerCategory2,
    Item,
    [Display(Name = "Group Size")]
    GroupSize,
    Fabric
}
