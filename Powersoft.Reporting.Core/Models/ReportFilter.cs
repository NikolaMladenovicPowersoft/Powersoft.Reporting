using System.ComponentModel.DataAnnotations;
using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

public class ReportFilter : IValidatableObject
{
    [Required(ErrorMessage = "Date From is required")]
    [DataType(DataType.Date)]
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    
    [Required(ErrorMessage = "Date To is required")]
    [DataType(DataType.Date)]
    public DateTime DateTo { get; set; } = DateTime.Today;
    
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    public GroupByType SecondaryGroupBy { get; set; } = GroupByType.None;
    public bool IncludeVat { get; set; } = false;
    public bool CompareLastYear { get; set; } = false;
    
    public List<string> StoreCodes { get; set; } = new();
    
    [Range(1, int.MaxValue, ErrorMessage = "Page number must be at least 1")]
    public int PageNumber { get; set; } = 1;
    
    [Range(1, 1000, ErrorMessage = "Page size must be between 1 and 1000")]
    public int PageSize { get; set; } = 50;
    
    public string? DatePreset { get; set; }
    
    private string _sortColumn = "Period";
    public string SortColumn
    {
        get => _sortColumn;
        set => _sortColumn = ValidSortColumns.Contains(value, StringComparer.OrdinalIgnoreCase) ? value : "Period";
    }
    
    private string _sortDirection = "ASC";
    public string SortDirection
    {
        get => _sortDirection;
        set => _sortDirection = string.Equals(value, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
    }
    
    private static readonly HashSet<string> ValidSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Period", "GroupName", "Invoices", "Returns", "NetTransactions",
        "QtySold", "QtyReturned", "NetQty", "Sales", "AvgBasket", "AvgQty"
    };
    
    public Dictionary<string, string> FilterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FilterOperators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public bool HasStoreFilter => StoreCodes.Any();
    public bool HasColumnFilters => FilterValues.Any(kv => !string.IsNullOrEmpty(kv.Value));
    
    public int Skip => (PageNumber - 1) * PageSize;
    
    public const int MaxDateRangeDays = 1095;
    
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom > DateTo)
        {
            yield return new ValidationResult(
                "Date From cannot be after Date To",
                new[] { nameof(DateFrom), nameof(DateTo) });
        }
        
        var dateRange = (DateTo - DateFrom).Days;
        if (dateRange > MaxDateRangeDays)
        {
            yield return new ValidationResult(
                $"Date range cannot exceed {MaxDateRangeDays} days (3 years)",
                new[] { nameof(DateFrom), nameof(DateTo) });
        }
        
        if (DateTo > DateTime.Today.AddDays(1))
        {
            yield return new ValidationResult(
                "Date To cannot be in the future",
                new[] { nameof(DateTo) });
        }
        
        if (DateFrom.Year < 2000)
        {
            yield return new ValidationResult(
                "Date From appears to be invalid (before year 2000)",
                new[] { nameof(DateFrom) });
        }
        
    }
    
    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();
        
        if (!Validator.TryValidateObject(this, context, results, validateAllProperties: true))
        {
            errors.AddRange(results.Select(r => r.ErrorMessage ?? "Validation error"));
        }
        
        return errors.Count == 0;
    }
}
