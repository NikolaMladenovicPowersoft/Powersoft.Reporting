using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ITrialBalanceRepository
{
    /// <summary>
    /// Builds the Trial Balance as at the filter date. Returns the per-account rows plus a flag
    /// indicating whether the "as at" date fell inside a defined fiscal year (tbl_acperiod).
    /// When no fiscal year matches, rows is empty and fiscalYearFound is false.
    /// </summary>
    Task<(List<TrialBalanceRow> rows, bool fiscalYearFound)> GenerateAsync(TrialBalanceFilter filter);

    /// <summary>All COA headers (tbl_COA) that own at least one detail account, for the selection pickers.</summary>
    Task<List<TrialBalanceHeaderOption>> GetHeadersAsync();

    /// <summary>Detail accounts (tbl_DetailAc), optionally restricted to the given header keys (CSV of pk_Key).</summary>
    Task<List<TrialBalanceAccountOption>> GetAccountsAsync(string? headersCsv);
}
