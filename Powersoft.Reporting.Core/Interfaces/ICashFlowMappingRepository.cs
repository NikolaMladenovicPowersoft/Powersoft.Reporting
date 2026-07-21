using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

/// <summary>
/// CRUD over dboReportsAI.tbl_CashFlowMapping (per-tenant Cash Flow section mapping).
/// Used by the admin UI; the Cash Flow report itself reads the table directly in
/// CashFlowRepository with most-specific-range-wins resolution.
/// </summary>
public interface ICashFlowMappingRepository
{
    Task<List<CashFlowMappingEntry>> GetAllAsync();
    Task<int> InsertAsync(CashFlowMappingEntry entry);
    Task<bool> UpdateAsync(CashFlowMappingEntry entry);
    Task<bool> DeleteAsync(int pkMappingID);

    /// <summary>Resolves one account code the same way the report does (most specific range wins).</summary>
    Task<CashFlowMappingResolution> ResolveAccountAsync(string accountCode);

    /// <summary>
    /// Coverage of the mapping against the tenant's chart of accounts (tbl_detailac):
    /// counts + the unmapped accounts (cash-active first), capped at <paramref name="maxUnassigned"/>.
    /// </summary>
    Task<CashFlowMappingCoverage> GetCoverageAsync(int maxUnassigned = 200);

    /// <summary>
    /// Preview of a candidate range: how many real accounts it catches (sample capped at
    /// <paramref name="maxSample"/>) and which existing ranges overlap it.
    /// <paramref name="excludeId"/> skips the row being edited from the overlap list.
    /// </summary>
    Task<CashFlowMappingRangePreview> PreviewRangeAsync(string codeFrom, string codeTo, int excludeId, int maxSample = 10);

    /// <summary>Deletes all rows and re-inserts the default (ARVA/PBIX) seed. Returns seeded row count.</summary>
    Task<int> ResetToDefaultsAsync();
}
