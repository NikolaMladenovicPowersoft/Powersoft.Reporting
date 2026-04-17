using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

public class CatalogueFilter : IValidatableObject
{
    [Required] [DataType(DataType.Date)]
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    [Required] [DataType(DataType.Date)]
    public DateTime DateTo { get; set; } = DateTime.Today;

    /// <summary>Transaction Date (h.DateTrans) vs Session Date (h.SessionDateTime).</summary>
    public CatalogueDateBasis DateBasis { get; set; } = CatalogueDateBasis.TransactionDate;

    /// <summary>When true, DateFrom/DateTo include time components (CONVERT(DATETIME) in SQL); otherwise date-only.</summary>
    public bool UseDateTime { get; set; } = false;

    public CatalogueReportMode ReportMode { get; set; } = CatalogueReportMode.Detailed;
    public CatalogueReportOn ReportOn { get; set; } = CatalogueReportOn.Sale;

    public CatalogueGroupBy PrimaryGroup { get; set; } = CatalogueGroupBy.None;
    public CatalogueGroupBy SecondaryGroup { get; set; } = CatalogueGroupBy.None;
    public CatalogueGroupBy ThirdGroup { get; set; } = CatalogueGroupBy.None;

    // --- Cost / Profit options ---
    public CatalogueCostBasis ProfitBasedOn { get; set; } = CatalogueCostBasis.LatestCost;
    public bool ProfitIncludesVat { get; set; }
    public CatalogueCostBasis StockValueBasedOn { get; set; } = CatalogueCostBasis.LatestCost;
    public bool StockValueIncludesVat { get; set; }
    public CatalogueCostBasis CostType { get; set; } = CatalogueCostBasis.CostOnSale;

    // --- Display value columns (tag chips) ---
    public List<string> DisplayColumns { get; set; } = new()
    {
        "ItemCode", "ItemName", "Quantity", "Value", "Discount",
        "NetValue", "VatAmount", "GrossAmount"
    };

    // --- Quick-toggle convenience flags (kept in sync with DisplayColumns by the UI/controller) ---
    // ShowProfit ↔ {Profit, Markup, Margin}; ShowStock ↔ {TotalStockQty, TotalStockValue}.
    // The actual SQL/grid logic uses DisplayColumns; these flags are passed through for export/email metadata.
    public bool ShowProfit { get; set; }
    public bool ShowStock { get; set; }

    // --- Filters ---
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
    public int MaxRecords { get; set; } = 10000;

    public bool IsSummary => ReportMode == CatalogueReportMode.Summary;

    public Dictionary<string, string> FilterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FilterOperators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasColumnFilters => FilterValues.Any(kv => !string.IsNullOrEmpty(kv.Value));

    public bool HasDisplayColumn(string col) =>
        DisplayColumns.Contains(col, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom > DateTo)
            yield return new ValidationResult("Date From cannot be after Date To",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if ((DateTo - DateFrom).Days > 1095)
            yield return new ValidationResult("Date range cannot exceed 3 years",
                new[] { nameof(DateFrom), nameof(DateTo) });

        if (PrimaryGroup != CatalogueGroupBy.None && SecondaryGroup != CatalogueGroupBy.None
            && PrimaryGroup == SecondaryGroup)
            yield return new ValidationResult("Primary and Secondary group must be different",
                new[] { nameof(SecondaryGroup) });

        if (ThirdGroup != CatalogueGroupBy.None)
        {
            if (PrimaryGroup != CatalogueGroupBy.None && ThirdGroup == PrimaryGroup)
                yield return new ValidationResult("Third group must be different from Primary group",
                    new[] { nameof(ThirdGroup) });
            if (SecondaryGroup != CatalogueGroupBy.None && ThirdGroup == SecondaryGroup)
                yield return new ValidationResult("Third group must be different from Secondary group",
                    new[] { nameof(ThirdGroup) });
        }

        if (DisplayColumns.Count == 0)
            yield return new ValidationResult("At least one display column must be selected",
                new[] { nameof(DisplayColumns) });
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
