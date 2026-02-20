using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICentralRepository
{
    // Existing — kept for backward compatibility / system admin usage
    Task<List<Company>> GetActiveCompaniesAsync();
    Task<List<Database>> GetActiveDatabasesForCompanyAsync(string companyCode);
    Task<Database?> GetDatabaseByCodeAsync(string dbCode);
    Task<bool> TestConnectionAsync();

    // New — module-aware, role-aware access
    /// <summary>
    /// For Ranking &lt; 15 (system admin): returns ALL active databases grouped by company.
    /// For Ranking >= 15 (client users): returns only databases where user is linked (tbl_RelUserDB),
    /// database is linked to RENGINEAI module (tbl_RelModuleDb), and both company/DB are active.
    /// </summary>
    Task<List<Database>> GetAccessibleDatabasesAsync(string userCode, int ranking);

    /// <summary>
    /// Checks if a specific user has access to a specific database,
    /// considering ranking, module linkage, and user-DB mapping.
    /// </summary>
    Task<bool> CanUserAccessDatabaseAsync(string userCode, int ranking, string dbCode);

    /// <summary>
    /// Checks if a role has a specific action assigned (for Ranking > 20 only).
    /// </summary>
    Task<bool> IsActionAuthorizedAsync(int roleId, int actionId);
}
