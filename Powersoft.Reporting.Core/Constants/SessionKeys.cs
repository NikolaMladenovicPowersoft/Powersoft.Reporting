namespace Powersoft.Reporting.Core.Constants;

public static class SessionKeys
{
    public const string TenantConnectionString = "TenantConnectionString";
    public const string ConnectedDatabase = "ConnectedDatabase";
    public const string ConnectedDatabaseCode = "ConnectedDatabaseCode";

    public const string UserCode = "UserCode";
    public const string RoleID = "RoleID";
    public const string Ranking = "Ranking";
}

/// <summary>
/// Custom claim types used in the authentication cookie.
/// </summary>
public static class AppClaimTypes
{
    public const string UserCode = "ps:usercode";
    public const string RoleID = "ps:roleid";
    public const string Ranking = "ps:ranking";
    public const string RoleName = "ps:rolename";
}

/// <summary>
/// Module and action constants — coordinated with Christina.
/// </summary>
public static class ModuleConstants
{
    public const string ModuleCode = "RENGINEAI";

    public const int ActionViewAvgBasket = 6025;
    public const int ActionScheduleAvgBasket = 6026;

    /// <summary>
    /// Ranking threshold for DB filtering. Users with Ranking below this value (1, 5, 10)
    /// are system/support staff and see ALL companies and databases.
    /// Users at or above this value (15, 20, 21+) are client users and get filtered.
    /// </summary>
    public const int RankingSystemAdmin = 15;

    /// <summary>
    /// Ranking at or below this: all actions allowed (no per-action check needed).
    /// </summary>
    public const int RankingAllActionsAllowed = 20;

    // INI layout constants (tbl_IniModule / tbl_IniHeader codes)
    public const string IniHeaderAvgBasket = "AVGBASKET";
    public const string IniDescriptionAvgBasket = "Average Basket Report Layout";

    /// <summary>Default max active schedules per report type per DB. Overridable per-DB later (Priority 5).</summary>
    public const int ScheduleLimitDefault = 5;
}
