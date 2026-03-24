using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

public class PurchasesSalesFilter : IValidatableObject
{
    [Required] [DataType(DataType.Date)]
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    [Required] [DataType(DataType.Date)]
    public DateTime DateTo { get; set; } = DateTime.Today;

    public PsReportMode ReportMode { get; set; } = PsReportMode.Detailed;

    public PsGroupBy PrimaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy SecondaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy ThirdGroup { get; set; } = PsGroupBy.None;

    public bool IncludeVat { get; set; }
    public bool ShowProfit { get; set; }
    public bool ShowStock { get; set; }

    public List<string> StoreCodes { get; set; } = new();
    public List<int> ItemIds { get; set; } = new();
    public List<string> CategoryIds { get; set; } = new();
    public List<string> DepartmentIds { get; set; } = new();
    public List<string> SupplierIds { get; set; } = new();
    public List<string> BrandIds { get; set; } = new();
    public List<string> SeasonIds { get; set; } = new();

    public ItemsSelectionFilter? ItemsSelection { get; set; }

    public string SortColumn { get; set; } = "ItemCode";
    public string SortDirection { get; set; } = "ASC";

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int Skip => (PageNumber - 1) * PageSize;

    public bool IsSummary => ReportMode == PsReportMode.Summary
                          || (PrimaryGroup != PsGroupBy.None && ReportMode != PsReportMode.Detailed);
    public bool IsMonthly => ReportMode == PsReportMode.Monthly;

    public bool HasStoreFilter => StoreCodes.Any();
    public bool HasItemFilter => ItemIds.Any();
    public bool HasCategoryFilter => CategoryIds.Any();
    public bool HasDepartmentFilter => DepartmentIds.Any();
    public bool HasSupplierFilter => SupplierIds.Any();
    public bool HasBrandFilter => BrandIds.Any();
    public bool HasSeasonFilter => SeasonIds.Any();

    public Dictionary<string, string> FilterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FilterOperators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasColumnFilters => FilterValues.Any(kv => !string.IsNullOrEmpty(kv.Value));

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
