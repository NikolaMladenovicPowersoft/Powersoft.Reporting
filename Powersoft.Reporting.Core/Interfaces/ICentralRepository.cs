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

    /// <summary>
    /// Returns only databases linked to the RENGINEAI module (via tbl_RelModuleDb).
    /// Used by the scheduler to avoid scanning unrelated databases.
    /// </summary>
    Task<List<Database>> GetDatabasesLinkedToModuleAsync();

    /// <summary>
    /// Reads system settings from tbl_SystemSettings where ParameterCode starts with the given prefix.
    /// </summary>
    Task<Dictionary<string, string>> GetSystemSettingsAsync(string parameterPrefix);

    /// <summary>
    /// Upserts a system setting in tbl_SystemSettings.
    /// </summary>
    Task UpsertSystemSettingAsync(string parameterCode, string description, string dataType, string value);

    // ==================== AI usage tracking (cross-tenant, stored in psCentral) ====================

    /// <summary>
    /// Idempotently creates the central AI usage log table (dbo.tbl_RE_AiUsageLog) if missing.
    /// Best-effort: called once at startup. Failures (e.g. no DDL rights) must not crash the app.
    /// </summary>
    Task EnsureAiUsageLogSchemaAsync();

    /// <summary>
    /// Records one AI analysis event centrally. Best-effort — callers should swallow exceptions
    /// so that logging never breaks the analysis/email flow.
    /// </summary>
    Task LogAiUsageAsync(AiUsageLogEntry entry);

    /// <summary>
    /// Aggregates AI usage across all tenants for the given (inclusive) date range,
    /// broken down by company, report type and user.
    /// </summary>
    Task<AiUsageReport> GetAiUsageReportAsync(DateTime dateFrom, DateTime dateTo);

    /// <summary>
    /// Returns users (with email) who have access to a specific database.
    /// Used for populating email recipient pickers.
    /// </summary>
    Task<List<(string UserCode, string DisplayName, string Email)>> GetUsersForDatabaseAsync(string dbCode);
}
