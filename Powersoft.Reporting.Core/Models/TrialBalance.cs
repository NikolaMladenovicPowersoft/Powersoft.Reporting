namespace Powersoft.Reporting.Core.Models;

/// <summary>Trial Balance output mode: per-account detail or per-header summary.</summary>
public enum TrialBalanceReportMode { Detailed, Summary }

/// <summary>
/// Filter for the Trial Balance report. Ported 1:1 from legacy Powersoft.CloudAccounting
/// repTrialBalance.aspx.vb + Powersoft.CloudReports\TrialBalance.vb.
/// </summary>
public class TrialBalanceFilter
{
    /// <summary>"As at" date. Determines the fiscal year (tbl_acperiod) and the cut-off for balances.</summary>
    public DateTime AsAt { get; set; } = DateTime.Today;

    /// <summary>When false, accounts with no opening balance and no period movements are excluded.</summary>
    public bool IncludeZeroMovements { get; set; }

    /// <summary>Detailed (per account) vs Summary (per COA header).</summary>
    public TrialBalanceReportMode ReportMode { get; set; } = TrialBalanceReportMode.Detailed;

    /// <summary>CSV of tbl_DetailAc.pk_DetailID to restrict to (empty = all accounts).</summary>
    public string SelectedAccounts { get; set; } = "";

    /// <summary>CSV of tbl_COA.pk_Key headers to restrict to (empty = all headers).</summary>
    public string SelectedHeaders { get; set; } = "";

    /// <summary>CSV of tbl_COA.pk_Key headers whose detail lines are hidden but still counted in totals.</summary>
    public string SuppressedHeaders { get; set; } = "";

    public string SortColumn { get; set; } = "AccountCode";
    public string SortDirection { get; set; } = "ASC";
}

/// <summary>
/// One Trial Balance line (one detail account). Mirrors the legacy "AccountsMovement" DataTable.
/// Amounts are stored as absolute values; the side is carried in the *Type fields ("DR"/"CR"/"").
/// </summary>
public class TrialBalanceRow
{
    /// <summary>tbl_DetailAc.pk_DetailID (legacy da_code).</summary>
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";

    public decimal OpeningBalance { get; set; }
    public string OpeningBalanceType { get; set; } = "";

    public decimal DebitMovement { get; set; }
    public decimal CreditMovement { get; set; }

    public decimal ClosingBalance { get; set; }
    public string ClosingBalanceType { get; set; } = "";

    /// <summary>tbl_COA.pk_Key of the parent header.</summary>
    public int HeaderKey { get; set; }
    public string HeaderName { get; set; } = "";

    /// <summary>Display header code: Trim(ac_prefix)+Trim(ac_number).</summary>
    public string HeaderCode { get; set; } = "";

    /// <summary>Zero-padded sort key for the header code.</summary>
    public string HeaderCodeSort { get; set; } = "";

    /// <summary>True when the parent header is in the suppress list (row hidden in detail, kept in totals).</summary>
    public bool Suppressed { get; set; }
}

/// <summary>A COA header option (tbl_COA) for the header selection / suppression pickers.</summary>
public class TrialBalanceHeaderOption
{
    public int Key { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>A detail account option (tbl_DetailAc) for the account selection picker.</summary>
public class TrialBalanceAccountOption
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int HeaderKey { get; set; }
}
