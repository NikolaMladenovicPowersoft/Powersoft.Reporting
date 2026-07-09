namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Cash Flow (Direct) filter.
///
/// The engine mirrors the Power BI cash-flow logic 1:1 but is self-contained (no dependency on the
/// GetAllTransactionsForBowerBI / GetFullTreeCoaBI SQL functions, which only exist on PowerBI-linked
/// tenants). Cash-flow relevance = a journal transaction (fk_tt_number) that touches at least one
/// bank account (tbl_accbank) and is not an Opening Balance ('OB') or Year-End ('YE') entry. Every
/// leg of such a transaction is included and mapped to a statement Group/Category via
/// dboReportsAI.tbl_CashFlowMapping (COA code ranges, seeded from the PBI Accounting model).
/// </summary>
public class CashFlowFilter
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;

    /// <summary>Adds Prev. Year + Variance columns (same range shifted back one year). Ignored in monthly mode.</summary>
    public bool CompareToLastYear { get; set; }

    /// <summary>Adds Budget + Variance columns (tbl_AccBudgetDetails, months inside the period). Ignored in monthly mode.</summary>
    public bool IncludeBudget { get; set; }

    /// <summary>Show the individual accounts under each statement category (drill-down).</summary>
    public bool ShowAccounts { get; set; }

    /// <summary>
    /// Monthly breakdown: one Amount column per calendar month in the period (mirrors the PBI
    /// "Cash Flow Direct Monthly" page). Prior-year and budget columns are not available in this mode.
    /// </summary>
    public bool Monthly { get; set; }

    public bool IsValid() => DateFrom <= DateTo && (!Monthly || MonthCount <= 12);

    public int MonthCount =>
        (DateTo.Year - DateFrom.Year) * 12 + DateTo.Month - DateFrom.Month + 1;

    public DateTime PriorDateFrom => DateFrom.AddYears(-1);
    public DateTime PriorDateTo => DateTo.AddYears(-1);
}

/// <summary>
/// One cash-flow account line mapped to its statement Group/Category.
///
/// SIGN CONVENTION (verified to the cent against the Power BI Cash Flow Statement matrix, Jan 2025
/// and Jan 2022 columns of HE11901-ARVO-Accounting.pbix): every displayed amount is the NEGATED raw
/// journal sum (raw = DR positive / CR negative), for ALL lines — receipts positive under Cash In,
/// payments negative under Cash Out, Bank is the balancing counter-movement, and all groups sum to
/// exactly 0. (The PBI account-level drill-through page uses a different per-account sign and does
/// NOT sum to its own matrix — deliberate deviation: we keep the matrix sign everywhere so account
/// rows add up to their category subtotals.)
/// </summary>
public class CashFlowRow
{
    /// <summary>Statement section, e.g. "Operating Activities - Cash In". "(Unassigned)" when no mapping range matches.</summary>
    public string GroupName { get; set; } = "";
    public int GroupSortOrder { get; set; }

    /// <summary>Statement line inside the group, e.g. "Customers".</summary>
    public string CategoryName { get; set; } = "";
    public int CategorySortOrder { get; set; }

    /// <summary>COA header key of the account's parent (merge key for header-level budget lines).</summary>
    public long HeaderKey { get; set; }

    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";

    /// <summary>True when the account is a cash/bank account (tbl_accbank).</summary>
    public bool IsBank { get; set; }

    /// <summary>Current period amount (cash-flow display sign, see class remarks).</summary>
    public decimal Amount { get; set; }

    /// <summary>Prior-year amount (same sign convention). 0 unless CompareToLastYear.</summary>
    public decimal PriorAmount { get; set; }

    /// <summary>Budget amount for the period months (same sign convention). 0 unless IncludeBudget.</summary>
    public decimal BudgetAmount { get; set; }

    /// <summary>Monthly mode: amount per month key ("yyyy-MM"). Null in normal mode.</summary>
    public Dictionary<string, decimal>? MonthAmounts { get; set; }

    public decimal Variance => Amount - PriorAmount;
    public decimal BudgetVariance => Amount - BudgetAmount;
}

/// <summary>One statement category defined in dboReportsAI.tbl_CashFlowMapping (skeleton row — shown even when it has no data, like the PBI matrix).</summary>
public class CashFlowMappingCategory
{
    public string GroupName { get; set; } = "";
    public int GroupSortOrder { get; set; }
    public string CategoryName { get; set; } = "";
    public int CategorySortOrder { get; set; }
}

/// <summary>Repository output: account-level rows + the mapping skeleton + month keys (monthly mode).</summary>
public class CashFlowResult
{
    public List<CashFlowRow> Rows { get; set; } = new();
    public List<CashFlowMappingCategory> Categories { get; set; } = new();
    /// <summary>Month keys ("yyyy-MM") spanning the period. Empty unless monthly mode.</summary>
    public List<string> Months { get; set; } = new();
}

// ---------- Presentation statement (shared by view JSON, print preview and all exports) ----------

public class CashFlowStatement
{
    public List<CashFlowStatementGroup> Groups { get; set; } = new();
    public List<string> Months { get; set; } = new();

    /// <summary>Net cash movement = -sum of bank-account legs (all legs of a journal sum to 0).</summary>
    public decimal NetCashMovement { get; set; }
    public decimal PriorNetCashMovement { get; set; }

    /// <summary>Monthly mode: net cash movement per month key (-sum of bank legs that month).</summary>
    public Dictionary<string, decimal> MonthNetCashMovement { get; set; } = new();
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public int AccountRowCount { get; set; }
}

public class CashFlowStatementGroup
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<CashFlowStatementCategory> Categories { get; set; } = new();

    public decimal Amount { get; set; }
    public decimal PriorAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public Dictionary<string, decimal>? MonthAmounts { get; set; }
    public decimal Variance => Amount - PriorAmount;
    public decimal BudgetVariance => Amount - BudgetAmount;
}

public class CashFlowStatementCategory
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    /// <summary>Account rows under this category (populated only when ShowAccounts).</summary>
    public List<CashFlowRow> Accounts { get; set; } = new();

    /// <summary>True when no transaction hit this category in any column (renders as "-").</summary>
    public bool IsEmpty { get; set; }

    public decimal Amount { get; set; }
    public decimal PriorAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public Dictionary<string, decimal>? MonthAmounts { get; set; }
    public decimal Variance => Amount - PriorAmount;
    public decimal BudgetVariance => Amount - BudgetAmount;
}
