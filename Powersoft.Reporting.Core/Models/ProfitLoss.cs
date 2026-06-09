namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Profit &amp; Loss group, mirroring legacy Powersoft.CloudReports\ProfitAndLoss.vb (nGroup 1-4).
/// Order is the legacy presentation order: Sales, Cost of Sales, Income, Expenses.
/// </summary>
public enum ProfitLossGroup
{
    Sales = 1,
    CostOfSales = 2,
    Income = 3,
    Expenses = 4
}

/// <summary>
/// Parameters for the Profit &amp; Loss statement. Mirrors the legacy repProfitAndLoss.aspx.vb form:
/// date range, header-level aggregation, suppressed headers, and manual opening/closing stock.
/// Adds an optional prior-year comparison column (same date range shifted back one year).
/// </summary>
public class ProfitLossFilter
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;

    /// <summary>Aggregate detail accounts up to their parent header (legacy chkHeaderLevel).</summary>
    public bool HeaderLevel { get; set; }

    /// <summary>CSV of tbl_COA.pk_Key to suppress (hide). Legacy chkSelectHeaders + selection dialog.</summary>
    public string SuppressedHeaders { get; set; } = "";

    /// <summary>Manual opening stock value (DR) injected into Cost of Sales. Legacy txtOpenStock.</summary>
    public decimal OpeningStockValue { get; set; }

    /// <summary>Manual closing stock value (CR) injected into Cost of Sales. Legacy txtCloseStock.</summary>
    public decimal ClosingStockValue { get; set; }

    /// <summary>When true, also computes the same date range shifted back exactly one year for comparison.</summary>
    public bool CompareToLastYear { get; set; }

    public string SortColumn { get; set; } = "AccountCode";
    public string SortDirection { get; set; } = "ASC";

    public bool IsValid() => DateFrom <= DateTo;

    /// <summary>The comparison "from" date: same range shifted back one year.</summary>
    public DateTime PriorDateFrom => DateFrom.AddYears(-1);

    /// <summary>The comparison "to" date: same range shifted back one year.</summary>
    public DateTime PriorDateTo => DateTo.AddYears(-1);
}

/// <summary>
/// One Profit &amp; Loss line. Balance is the legacy "nRealBalance" (already sign-normalized per group:
/// positive = favourable contribution to profit). Prior is the same account for the prior-year period.
/// </summary>
public class ProfitLossRow
{
    public ProfitLossGroup Group { get; set; }
    public string GroupName { get; set; } = "";

    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";

    /// <summary>tbl_COA.pk_Key of the parent header.</summary>
    public int HeaderKey { get; set; }
    public string HeaderName { get; set; } = "";

    /// <summary>"ac_prefix + ac_number - pk_detailid" — legacy cPrefNo, used for sorting.</summary>
    public string PrefNo { get; set; } = "";

    /// <summary>Sign-normalized balance for the current period.</summary>
    public decimal Balance { get; set; }

    /// <summary>Sign-normalized balance for the prior-year period (0 if comparison off or no data).</summary>
    public decimal PriorBalance { get; set; }

    /// <summary>Variance = current - prior.</summary>
    public decimal Variance => Balance - PriorBalance;

    /// <summary>Variance percentage relative to the prior period (null when prior is 0).</summary>
    public decimal? VariancePercent =>
        PriorBalance == 0 ? null : Math.Round((Balance - PriorBalance) / Math.Abs(PriorBalance) * 100m, 2);

    public bool Suppressed { get; set; }
}

/// <summary>
/// Header/COA option for the suppress-headers selection picker.
/// </summary>
public class ProfitLossHeaderOption
{
    public int Key { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
