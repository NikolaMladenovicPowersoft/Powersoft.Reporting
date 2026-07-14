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

    /// <summary>Deletes all rows and re-inserts the default (ARVA/PBIX) seed. Returns seeded row count.</summary>
    Task<int> ResetToDefaultsAsync();
}
