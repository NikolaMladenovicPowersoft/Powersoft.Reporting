using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Filter for the "Items Not Purchased (by Customer) in X Days" report (Report B).
///
/// Default semantics (PENDING George's confirmation — this report has no legacy parity source):
///  - "Not purchased" = an item whose most recent sale in scope is older than <see cref="DaysThreshold"/>
///    days measured back from <see cref="ReferenceDate"/> (mirrors the Power BI DAX DATEDIFF(LastSale, TODAY())).
///  - Scope of "sale" = sales to the selected <see cref="CustomerCodes"/>; when empty, sales to ANY customer.
///  - <see cref="IncludeNeverPurchased"/> = false by default: only items that WERE sold in the observation
///    window but have since gone quiet (George: "...when it was last bought"). Set true to also list items
///    never sold in scope.
///  - <see cref="DateFrom"/>/<see cref="DateTo"/> bound the observation window used to establish last-sale.
/// </summary>
public class CustomerNotPurchasedFilter : IValidatableObject
{
    [Required] [DataType(DataType.Date)]
    public DateTime DateFrom { get; set; } = DateTime.Today.AddYears(-2);

    [Required] [DataType(DataType.Date)]
    public DateTime DateTo { get; set; } = DateTime.Today;

    /// <summary>Point from which "X days ago" is measured. Defaults to today (Power BI parity).</summary>
    [DataType(DataType.Date)]
    public DateTime ReferenceDate { get; set; } = DateTime.Today;

    /// <summary>Staleness threshold in days. Item is "not purchased" when last sale is more than this many days ago.</summary>
    [Range(1, 3650, ErrorMessage = "Days must be between 1 and 3650")]
    public int DaysThreshold { get; set; } = 30;

    /// <summary>Selected customers. Empty = all customers.</summary>
    public List<string> CustomerCodes { get; set; } = new();

    /// <summary>When true, <see cref="CustomerCodes"/> are excluded rather than the sole set considered.</summary>
    public bool CustomerExcludeMode { get; set; } = false;

    /// <summary>Also include items with no sale at all in scope (last-purchase date null).</summary>
    public bool IncludeNeverPurchased { get; set; } = false;

    /// <summary>Row grouping. Supported: None, Item, Customer, Category. Default Item.</summary>
    public GroupByType GroupBy { get; set; } = GroupByType.Item;

    // --- Item / dimension scope (shared _ItemsSelection partial) ---
    public List<string> StoreCodes { get; set; } = new();
    public List<int> ItemIds { get; set; } = new();
    public ItemsSelectionFilter? ItemsSelection { get; set; }

    public string SortColumn { get; set; } = "DaysSinceLastPurchase";
    public string SortDirection { get; set; } = "DESC";

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int Skip => (PageNumber - 1) * PageSize;
    public int MaxRecords { get; set; } = 10000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom > DateTo)
            yield return new ValidationResult("Date From cannot be after Date To",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if ((DateTo - DateFrom).Days > 1095)
            yield return new ValidationResult("Observation window cannot exceed 3 years",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if (ReferenceDate > DateTime.Today.AddDays(1))
            yield return new ValidationResult("Reference date cannot be in the future",
                new[] { nameof(ReferenceDate) });

        if (GroupBy is not (GroupByType.None or GroupByType.Item or GroupByType.Customer or GroupByType.Category))
            yield return new ValidationResult("Grouping must be None, Item, Customer or Category",
                new[] { nameof(GroupBy) });
    }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        var ctx = new ValidationContext(this);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(this, ctx, results, true))
            errors.AddRange(results.Select(r => r.ErrorMessage ?? "Validation error"));
        return errors.Count == 0;
    }
}
