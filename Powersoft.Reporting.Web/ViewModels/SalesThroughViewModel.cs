using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

/// <summary>
/// Model for the Sales Through print preview. Carries the fully-materialised
/// (unpaged) rows, grand totals and the filter used to produce them so the print
/// view can render headers/period without another DB round-trip.
/// </summary>
public class SalesThroughViewModel
{
    public List<SalesThroughRow> Rows { get; set; } = new();
    public SalesThroughTotals? Totals { get; set; }
    public SalesThroughFilter Filter { get; set; } = new();
    public string? ConnectedDatabase { get; set; }

    public bool HasL1 => Filter.PrimaryGroup != Core.Enums.PsGroupBy.None;
    public bool HasL2 => Filter.SecondaryGroup != Core.Enums.PsGroupBy.None;
    public bool HasL3 => Filter.ThirdGroup != Core.Enums.PsGroupBy.None;
    public bool HasItem => !Filter.IsSummary || (!HasL1 && !HasL2 && !HasL3);
}
