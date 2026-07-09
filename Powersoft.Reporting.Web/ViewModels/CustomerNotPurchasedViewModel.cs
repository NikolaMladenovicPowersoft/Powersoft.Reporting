using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

/// <summary>
/// Model for the Items Not Purchased print preview. Carries the fully-materialised
/// (unpaged) rows plus the filter used to produce them so the print view can render
/// headers/period without another DB round-trip.
/// </summary>
public class CustomerNotPurchasedViewModel
{
    public List<CustomerNotPurchasedRow> Rows { get; set; } = new();
    public CustomerNotPurchasedFilter Filter { get; set; } = new();
    public string? ConnectedDatabase { get; set; }

    public bool GroupByCustomer =>
        Filter.GroupBy == Core.Enums.GroupByType.Customer;
}
