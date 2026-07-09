namespace Powersoft.Reporting.Core.Constants;

public static class SessionKeys
{
    public const string TenantConnectionString = "TenantConnectionString";
    public const string ConnectedDatabase = "ConnectedDatabase";
    public const string ConnectedDatabaseCode = "ConnectedDatabaseCode";

    public const string UserCode = "UserCode";
    public const string RoleID = "RoleID";
    public const string Ranking = "Ranking";

    // Permission flags resolved at login from tbl_RelRoleAction
    public const string ViewCost = "ViewCost";
    public const string ViewSupplier = "ViewSupplier";
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

    // Permission flags (resolved at login, stored in auth cookie)
    public const string ViewCost = "ps:viewcost";
    public const string ViewSupplier = "ps:viewsupplier";
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
        public const int ActionViewPareto = 6029;
        public const int ActionSchedulePareto = 6030;
        public const int ActionViewCharts = 6031;
        public const int ActionScheduleCharts = 6032;
        public const int ActionViewCatalogue = 6033;
        public const int ActionScheduleCatalogue = 6034;
    public const int ActionViewProspectClients = 6035;
    public const int ActionScheduleProspectClients = 6036;
    public const int ActionViewOffersReport = 6037;
    public const int ActionScheduleOffersReport = 6038;
    public const int ActionViewBelowMinStock = 6039;
    public const int ActionScheduleBelowMinStock = 6040;
    public const int ActionViewCancelLog = 6041;
    public const int ActionScheduleCancelLog = 6042;
    public const int ActionViewTrialBalance = 6043;
    public const int ActionScheduleTrialBalance = 6044;
    public const int ActionViewProfitLoss = 6045;
    public const int ActionScheduleProfitLoss = 6046;
    // Report B — Items Not Purchased (by Customer). ⚠ These IDs must be seeded in psCentral tbl_Action
    // by Christina before go-live (same as all other action IDs here). Placeholder next-in-sequence values.
    public const int ActionViewCustomerNotPurchased = 6047;
    public const int ActionScheduleCustomerNotPurchased = 6048;
    // Cash Flow (Direct). ⚠ Seed in psCentral tbl_Action before go-live (Christina), same as above.
    public const int ActionViewCashFlow = 6049;
    public const int ActionScheduleCashFlow = 6050;

    // Legacy Powersoft365 cross-module actions (already exist in psCentral — do NOT seed)
    public const int ActionViewCost = 6015;
    public const int ActionViewSupplierList = 1200;

    /// <summary>
    /// Legacy generic "View PowerReports" action (pk_ActionID=5100 in tbl_Action).
    /// If a role has this, grant access to ALL reports in our app.
    /// </summary>
    public const int ActionViewPowerReportsLegacy = 5100;

    /// <summary>
    /// Ranking threshold for DB filtering. Users with Ranking below this value (1, 5, 10)
    /// are system/support staff and see ALL companies and databases.
    /// Users at or above this value (15, 20, 21+) are client users and get filtered.
    /// </summary>
    /// <summary>Ranking of the top-level Powersoft webmaster/super-admin. Only this rank can see cross-tenant reports (AI usage).</summary>
    public const int RankingWebmaster = 1;
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
    public const string IniHeaderProspectClients = "PROSPECTS";
    public const string IniDescriptionProspectClients = "Prospect Clients Report Layout";
    public const string IniHeaderOffersReport = "OFFERS";
    public const string IniDescriptionOffersReport = "Offers Report Layout";
    public const string IniHeaderBelowMinStock = "BELOWMIN";
    public const string IniDescriptionBelowMinStock = "Below Min Stock Report Layout";
    public const string IniHeaderTrialBalance = "TRIALBAL";
    public const string IniDescriptionTrialBalance = "Trial Balance Report Layout";
    public const string IniHeaderProfitLoss = "PROFITLOSS";
    public const string IniDescriptionProfitLoss = "Profit & Loss Report Layout";
    public const string IniHeaderCustomerNotPurchased = "NOTPURCH";
    public const string IniDescriptionCustomerNotPurchased = "Items Not Purchased Report Layout";
    public const string IniHeaderCashFlow = "CASHFLOW";
    public const string IniDescriptionCashFlow = "Cash Flow Report Layout";

    // INI settings constants (DB-level settings, userCode = "ALL")
    public const string IniHeaderDbSettings = "DBSETTINGS";
    public const string IniDescriptionDbSettings = "Report Engine Database Settings";

    // Applied template packs (DB-level, userCode = "ALL"). Kept in a SEPARATE header from DBSETTINGS
    // so saving DB settings (which rewrites all DBSETTINGS details) cannot wipe the applied-pack log.
    // ParmCode = pack code (e.g. "FASHION"), ParmValue = ISO timestamp when it was applied.
    public const string IniHeaderAppliedPacks = "APPLIEDPACKS";
    public const string IniDescriptionAppliedPacks = "Applied Report Template Packs";

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
