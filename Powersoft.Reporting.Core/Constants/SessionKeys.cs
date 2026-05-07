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
    public const int ActionViewPurchasesSales = 6027;
    public const int ActionSchedulePurchasesSales = 6028;
    public const int ActionViewCatalogue = 6029;
    public const int ActionScheduleCatalogue = 6030;
    public const int ActionViewPareto = 6031;
    public const int ActionSchedulePareto = 6032;
    public const int ActionViewCharts = 6033;
    public const int ActionScheduleCharts = 6034;

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
    public const string IniHeaderPurchasesSales = "PURCHSALES";
    public const string IniDescriptionPurchasesSales = "Purchases vs Sales Report Layout";
    public const string IniHeaderCatalogue = "CATALOGUE";
    public const string IniDescriptionCatalogue = "Power Reports Catalogue Layout";
    public const string IniHeaderPareto = "PARETO8020";
    public const string IniDescriptionPareto = "Pareto 80/20 Report Layout";
    public const string IniHeaderCharts = "CHARTS";
    public const string IniDescriptionCharts = "Charts & Dashboards Layout";
    public const string IniHeaderCancelLog = "CANCELLOG";
    public const string IniDescriptionCancelLog = "Cancellation Logging Report Layout";

    // INI settings constants (DB-level settings, userCode = "ALL")
    public const string IniHeaderDbSettings = "DBSETTINGS";
    public const string IniDescriptionDbSettings = "Report Engine Database Settings";

    // Settings parameter codes (ParmCode in tbl_IniDetail)
    public const string SettingMaxSchedules = "MaxSchedulesPerReport";
    public const string SettingDefaultExportFormat = "DefaultExportFormat";
    public const string SettingSchedulerEnabled = "SchedulerEnabled";
    public const string SettingRetentionDays = "RetentionDays";

    /// <summary>Default max active schedules per report type per DB. Overridable via DB settings.</summary>
    public const int ScheduleLimitDefault = 5;
    public const string DefaultExportFormatValue = "Excel";
    public const int DefaultRetentionDays = 7;
}
