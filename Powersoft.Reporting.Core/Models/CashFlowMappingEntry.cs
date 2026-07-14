namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// One row of dboReportsAI.tbl_CashFlowMapping — maps an inclusive COA account-code range
/// (string comparison, PBI semantics) to a Cash Flow statement Group/Category.
/// Managed by administrators via the Cash Flow Mapping admin page (George, 2026-07-14:
/// client-specific ranges must be editable without code changes).
/// </summary>
public class CashFlowMappingEntry
{
    public int PkMappingID { get; set; }
    public string GroupName { get; set; } = "";
    public int GroupSortOrder { get; set; }
    public string CategoryName { get; set; } = "";
    public int CategorySortOrder { get; set; }
    public string CodeFrom { get; set; } = "";
    public string CodeTo { get; set; } = "";

    /// <summary>
    /// Validates the entry for saving. Returns null when valid, otherwise a user-facing message.
    /// Matching is an ORDINAL STRING comparison (same as the SQL/PBI range match), so a range
    /// whose CodeFrom sorts after CodeTo can never match any account.
    /// </summary>
    public string? Validate()
    {
        GroupName = (GroupName ?? "").Trim();
        CategoryName = (CategoryName ?? "").Trim();
        CodeFrom = (CodeFrom ?? "").Trim();
        CodeTo = (CodeTo ?? "").Trim();

        if (GroupName.Length == 0) return "Group name is required.";
        if (GroupName.Length > 60) return "Group name must be 60 characters or fewer.";
        if (CategoryName.Length == 0) return "Category name is required.";
        if (CategoryName.Length > 60) return "Category name must be 60 characters or fewer.";
        if (CodeFrom.Length == 0) return "Code From is required.";
        if (CodeFrom.Length > 20) return "Code From must be 20 characters or fewer.";
        if (CodeTo.Length == 0) return "Code To is required.";
        if (CodeTo.Length > 20) return "Code To must be 20 characters or fewer.";
        if (string.CompareOrdinal(CodeFrom, CodeTo) > 0)
            return "Code From sorts after Code To — this range would never match any account " +
                   "(ranges are compared as text, the same way the report resolves accounts).";
        return null;
    }
}

/// <summary>Result of resolving a single account code against the mapping (admin "test" tool).</summary>
public class CashFlowMappingResolution
{
    public bool Matched { get; set; }
    public string? GroupName { get; set; }
    public string? CategoryName { get; set; }
    /// <summary>The winning range (most specific: greatest CodeFrom, then sort orders).</summary>
    public int? PkMappingID { get; set; }
    public string? CodeFrom { get; set; }
    public string? CodeTo { get; set; }
    /// <summary>Total number of ranges that matched (before most-specific-wins picked one).</summary>
    public int MatchCount { get; set; }
}
