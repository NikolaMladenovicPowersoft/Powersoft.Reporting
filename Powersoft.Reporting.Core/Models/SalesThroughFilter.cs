using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Filter for the Sales Through report (Splash/George 2026-07).
/// Reuses the Purchases vs Sales grouping dimensions (<see cref="PsGroupBy"/>) because the
/// source data is the same engine: purchases (intake) vs sales vs current stock, with
/// sell-through and mix percentages added.
/// </summary>
public class SalesThroughFilter : IValidatableObject
{
    [Required] [DataType(DataType.Date)]
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    [Required] [DataType(DataType.Date)]
    public DateTime DateTo { get; set; } = DateTime.Today;

    /// <summary>Detailed = item-level rows; Summary = one row per group combination.</summary>
    public bool Summary { get; set; }

    public PsGroupBy PrimaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy SecondaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy ThirdGroup { get; set; } = PsGroupBy.None;

    /// <summary>
    /// When false, intake cost = purchase invoice net only (wholesale) — the Splash convention.
    /// When true, allocated additional/landed charges (tbl_CostingDetails) are included,
    /// matching legacy Powersoft365 Purchases &amp; Sales.
    /// </summary>
    public bool IncludeAdditionalCharges { get; set; } = true;

    /// <summary>Order Size groups by tbl_SizeSequence instead of alphabetically (Splash).</summary>
    public bool SortBySizeSequence { get; set; }

    public List<string> StoreCodes { get; set; } = new();
    public ItemsSelectionFilter? ItemsSelection { get; set; }

    public string SortColumn { get; set; } = "ItemCode";
    public string SortDirection { get; set; } = "ASC";

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int Skip => (PageNumber - 1) * PageSize;

    public bool IsSummary => Summary && PrimaryGroup != PsGroupBy.None;
    public bool HasStoreFilter => StoreCodes.Any();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom > DateTo)
            yield return new ValidationResult("Date From cannot be after Date To",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if ((DateTo - DateFrom).Days > 1095)
            yield return new ValidationResult("Date range cannot exceed 3 years",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if (PrimaryGroup != PsGroupBy.None && SecondaryGroup != PsGroupBy.None
            && PrimaryGroup == SecondaryGroup)
            yield return new ValidationResult("Primary and Secondary group must be different",
                new[] { nameof(SecondaryGroup) });

        if (ThirdGroup != PsGroupBy.None)
        {
            if (PrimaryGroup != PsGroupBy.None && ThirdGroup == PrimaryGroup)
                yield return new ValidationResult("Third group must be different from Primary group",
                    new[] { nameof(ThirdGroup) });
            if (SecondaryGroup != PsGroupBy.None && ThirdGroup == SecondaryGroup)
                yield return new ValidationResult("Third group must be different from Secondary group",
                    new[] { nameof(ThirdGroup) });
        }
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
