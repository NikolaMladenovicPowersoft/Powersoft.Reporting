using Microsoft.Data.SqlClient;

namespace Powersoft.Reporting.Data.Tenant;

public class SchemaMigrationService
{
    public static Action<string>? LogInfo { get; set; }
    public static Action<string, Exception>? LogWarning { get; set; }

    private const string SchemaName = "dboReportsAI";

    private const string DefaultSubject = "\u00ABReportName\u00BB \u2014 \u00ABDatabaseName\u00BB (\u00ABPeriod\u00BB)";

    private const string DefaultBody =
        "<div style=\"font-family:''Segoe UI'',Arial,sans-serif;max-width:640px;margin:0 auto;\">" +
        "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>" +
        "<td style=\"background-color:#1e40af;padding:24px 32px;\">" +
        "<h1 style=\"margin:0;color:#ffffff;font-size:20px;font-weight:600;\">Powersoft Reports</h1>" +
        "</td></tr></table>" +
        "<div style=\"background-color:#ffffff;padding:28px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;\">" +
        "<h2 style=\"margin:0 0 8px;color:#1e40af;font-size:18px;font-weight:700;\">\u00ABReportName\u00BB</h2>" +
        "<p style=\"margin:0 0 20px;color:#374151;font-size:14px;\">\u00ABDatabaseName\u00BB</p>" +
        "<p style=\"margin:0 0 16px;font-size:14px;line-height:1.6;color:#111827;\">" +
        "Please find the attached <strong>\u00ABReportName\u00BB</strong> report.</p>" +
        "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 20px;font-size:14px;\">" +
        "<tr><td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;width:120px;\">Period</td>" +
        "<td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;font-weight:600;\">\u00ABPeriod\u00BB</td></tr>" +
        "<tr><td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;\">Rows</td>" +
        "<td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;\">\u00ABRowCount\u00BB</td></tr>" +
        "<tr><td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;\">Format</td>" +
        "<td style=\"padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;\">\u00ABExportFormat\u00BB</td></tr>" +
        "<tr><td style=\"padding:10px 14px;color:#6b7280;\">Generated</td>" +
        "<td style=\"padding:10px 14px;color:#111827;\">\u00ABGeneratedDate\u00BB</td></tr></table></div>" +
        "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>" +
        "<td style=\"background-color:#f3f4f6;padding:16px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;border-bottom:1px solid #d1d5db;\">" +
        "<p style=\"margin:0;color:#6b7280;font-size:11px;\">" +
        "Automated report by Powersoft Report Engine &bull; \u00ABCompanyName\u00BB</p>" +
        "</td></tr></table></div>";

    private static readonly string MigrationSql = $@"
-- 1. Schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{SchemaName}')
    EXEC('CREATE SCHEMA {SchemaName}');

-- 2. tbl_ReportSchedule
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_ReportSchedule') AND type = 'U')
    CREATE TABLE {SchemaName}.tbl_ReportSchedule (
        pk_ScheduleID       INT IDENTITY(1,1) PRIMARY KEY,
        ReportType           NVARCHAR(100) NOT NULL,
        ScheduleName         NVARCHAR(200) NOT NULL,
        CreatedBy            NVARCHAR(50)  NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        IsActive             BIT           NOT NULL DEFAULT 1,
        RecurrenceType       NVARCHAR(20)  NOT NULL,
        RecurrenceDay        INT           NULL,
        ScheduleTime         TIME          NOT NULL DEFAULT '08:00',
        NextRunDate          DATETIME      NULL,
        LastRunDate          DATETIME      NULL,
        ParametersJson       NVARCHAR(MAX) NULL,
        RecurrenceJson       NVARCHAR(MAX) NULL,
        ExportFormat         NVARCHAR(10)  NOT NULL DEFAULT 'Excel',
        Recipients           NVARCHAR(MAX) NOT NULL,
        EmailSubject         NVARCHAR(500) NULL,
        IncludeAiAnalysis    BIT           NOT NULL DEFAULT 0,
        AiLocale             NVARCHAR(10)  NOT NULL DEFAULT 'el',
        SkipIfEmpty          BIT           NOT NULL DEFAULT 0,
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(50)  NULL
    );

-- 3. tbl_ReportEmailTemplate
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_ReportEmailTemplate') AND type = 'U')
BEGIN
    CREATE TABLE {SchemaName}.tbl_ReportEmailTemplate (
        pk_TemplateID       INT IDENTITY(1,1) PRIMARY KEY,
        TemplateName         NVARCHAR(100) NOT NULL,
        ReportType           NVARCHAR(100) NULL,
        EmailSubject         NVARCHAR(500) NOT NULL DEFAULT '',
        EmailBodyHtml        NVARCHAR(MAX) NOT NULL DEFAULT '',
        IsDefault            BIT           NOT NULL DEFAULT 0,
        IsActive             BIT           NOT NULL DEFAULT 1,
        CreatedBy            NVARCHAR(50)  NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(50)  NULL
    );
    INSERT INTO {SchemaName}.tbl_ReportEmailTemplate (TemplateName, EmailSubject, EmailBodyHtml, IsDefault, CreatedBy)
    VALUES ('Default Report Template',
            N'{DefaultSubject}',
            N'{DefaultBody}',
            1, 'SYSTEM');
END
ELSE
BEGIN
    UPDATE {SchemaName}.tbl_ReportEmailTemplate
    SET    EmailSubject  = N'{DefaultSubject}',
           EmailBodyHtml = N'{DefaultBody}',
           ModifiedDate  = GETDATE(),
           ModifiedBy    = 'SYSTEM'
    WHERE  TemplateName = 'Default Report Template'
      AND  IsDefault = 1
      AND  (EmailBodyHtml LIKE '%[[]ReportName]%'
        OR  EmailBodyHtml LIKE '%&#171;ReportName&#187;%'
        OR  EmailBodyHtml LIKE '%linear-gradient%');
END

-- 4. tbl_ReportScheduleLog
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_ReportScheduleLog') AND type = 'U')
BEGIN
    CREATE TABLE {SchemaName}.tbl_ReportScheduleLog (
        pk_LogID        INT IDENTITY(1,1) PRIMARY KEY,
        fk_ScheduleID   INT          NOT NULL REFERENCES {SchemaName}.tbl_ReportSchedule(pk_ScheduleID),
        RunDate          DATETIME     NOT NULL DEFAULT GETDATE(),
        Status           NVARCHAR(20) NOT NULL,
        RowsGenerated    INT          NULL,
        FileSizeBytes    BIGINT       NULL,
        ErrorMessage     NVARCHAR(MAX) NULL,
        DurationMs       INT          NULL
    );
    CREATE INDEX IX_ScheduleLog_Schedule ON {SchemaName}.tbl_ReportScheduleLog(fk_ScheduleID, RunDate DESC);
END

-- 5. tbl_AiPromptTemplate
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_AiPromptTemplate') AND type = 'U')
    CREATE TABLE {SchemaName}.tbl_AiPromptTemplate (
        pk_TemplateID       INT IDENTITY(1,1) PRIMARY KEY,
        TemplateName         NVARCHAR(200) NOT NULL,
        ReportType           NVARCHAR(100) NULL,
        SystemPrompt         NVARCHAR(MAX) NOT NULL,
        IsDefault            BIT           NOT NULL DEFAULT 0,
        IsActive             BIT           NOT NULL DEFAULT 1,
        CreatedBy            NVARCHAR(100) NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(100) NULL
    );

-- 6. tbl_FilterPreset
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_FilterPreset') AND type = 'U')
BEGIN
    CREATE TABLE {SchemaName}.tbl_FilterPreset (
        pk_PresetID      INT IDENTITY(1,1) PRIMARY KEY,
        PresetName       NVARCHAR(200) NOT NULL,
        ReportType       NVARCHAR(100) NULL,
        FilterJson       NVARCHAR(MAX) NOT NULL,
        CreatedBy        NVARCHAR(50)  NOT NULL,
        CreatedDate      DATETIME      NOT NULL DEFAULT GETDATE(),
        ModifiedDate     DATETIME      NULL,
        IsShared         BIT           NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_FilterPreset_User ON {SchemaName}.tbl_FilterPreset(CreatedBy, ReportType);
END

-- 7. tbl_ReportScheduleLog — add token columns if missing
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_ReportScheduleLog') AND name = 'InputTokens')
    ALTER TABLE {SchemaName}.tbl_ReportScheduleLog ADD InputTokens INT NULL, OutputTokens INT NULL, EstimatedCost DECIMAL(10,6) NULL;

-- 8. tbl_AiTokenBudget — monthly per-company token budget
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_AiTokenBudget') AND type = 'U')
BEGIN
    CREATE TABLE {SchemaName}.tbl_AiTokenBudget (
        pk_BudgetID         INT IDENTITY(1,1) PRIMARY KEY,
        MonthlyTokenLimit   INT      NOT NULL DEFAULT 500000,
        CurrentMonthUsed    INT      NOT NULL DEFAULT 0,
        BudgetMonth         DATE     NOT NULL,
        LastUpdated         DATETIME NOT NULL DEFAULT GETDATE()
    );
    CREATE UNIQUE INDEX IX_AiTokenBudget_Month ON {SchemaName}.tbl_AiTokenBudget(BudgetMonth);
END

-- 8b. tbl_AiTokenBudget — add per-analysis cost guard columns if missing
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_AiTokenBudget') AND name = 'SoftCostLimit')
    ALTER TABLE {SchemaName}.tbl_AiTokenBudget
        ADD SoftCostLimit DECIMAL(10,4) NOT NULL DEFAULT 0.10,
            HardCostLimit DECIMAL(10,4) NOT NULL DEFAULT 0.25;

-- 9. tbl_EmailRecipientList (per-company address book)
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_EmailRecipientList') AND type = 'U')
BEGIN
    CREATE TABLE {SchemaName}.tbl_EmailRecipientList (
        pk_RecipientID  INT IDENTITY(1,1) PRIMARY KEY,
        EmailAddress    NVARCHAR(256) NOT NULL,
        DisplayName     NVARCHAR(256) NULL,
        IsActive        BIT           NOT NULL DEFAULT 1,
        CreatedDate     DATETIME      NOT NULL DEFAULT GETDATE(),
        CreatedBy       NVARCHAR(50)  NULL
    );
    CREATE UNIQUE INDEX IX_EmailRecipient_Email ON {SchemaName}.tbl_EmailRecipientList(EmailAddress) WHERE IsActive = 1;
END

-- 10. tbl_ReportSchedule — add StarRating column if missing (1-5, NULL = not rated)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_ReportSchedule') AND name = 'StarRating')
    ALTER TABLE {SchemaName}.tbl_ReportSchedule ADD StarRating TINYINT NULL;

-- 11. tbl_CashFlowMapping — Cash Flow statement section mapping (Group/Category -> COA code ranges).
--     Extracted 1:1 from the Power BI Accounting model (HE11901-ARVO-Accounting.pbix,
--     ChartCF_Direct table; source sheet XL_CoA_CashFlow in Accounting_Metadata.xlsx).
--     Matching is an INCLUSIVE STRING comparison: CodeFrom <= AccountCode <= CodeTo (same
--     semantics as the Power BI M range match). When an account matches several ranges the
--     MOST SPECIFIC one wins (greatest CodeFrom, then Group/Category sort order) — verified
--     to reproduce the PBIX bridge table exactly (all 4,945 account-line pairs).
--     Seeded only when empty so per-tenant customisations are preserved.
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'{SchemaName}.tbl_CashFlowMapping') AND type = 'U')
    CREATE TABLE {SchemaName}.tbl_CashFlowMapping (
        pk_MappingID      INT IDENTITY(1,1) PRIMARY KEY,
        GroupName         NVARCHAR(60) NOT NULL,
        GroupSortOrder    INT          NOT NULL,
        CategoryName      NVARCHAR(60) NOT NULL,
        CategorySortOrder INT          NOT NULL,
        CodeFrom          NVARCHAR(20) NOT NULL,
        CodeTo            NVARCHAR(20) NOT NULL
    );

IF NOT EXISTS (SELECT 1 FROM {SchemaName}.tbl_CashFlowMapping)
INSERT INTO {SchemaName}.tbl_CashFlowMapping
    (GroupName, GroupSortOrder, CategoryName, CategorySortOrder, CodeFrom, CodeTo)
VALUES
    (N'Operating Activities - Cash In', 1000, N'Customers', 1100, N'122', N'12299999'),
    (N'Operating Activities - Cash In', 1000, N'Customers', 1100, N'411', N'411999'),
    (N'Operating Activities - Cash In', 1000, N'Commisions', 1200, N'422', N'4229999'),
    (N'Operating Activities - Cash In', 1000, N'Subsidies', 1300, N'425', N'425999'),
    (N'Operating Activities - Cash In', 1000, N'Bank Interest and Taxes', 1400, N'421', N'421999'),
    (N'Operating Activities - Cash In', 1000, N'Bank Interest and Taxes', 1400, N'426', N'426999'),
    (N'Operating Activities - Cash In', 1000, N'Bank Interest and Taxes', 1400, N'2162', N'2162999'),
    (N'Operating Activities - Cash In', 1000, N'Other Income', 1500, N'423', N'424999'),
    (N'Operating Activities - Cash In', 1000, N'Other Income', 1500, N'427', N'429999'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'123029', N'123029'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'212003', N'212003'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'212022', N'212022'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'212026', N'212026'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'441002', N'441002'),
    (N'Operating Activities - Cash Out', 2000, N'Employees', 2000, N'444', N'444999'),
    (N'Operating Activities - Cash Out', 2000, N'Shareholders', 2100, N'3', N'399999'),
    (N'Operating Activities - Cash Out', 2000, N'Suppliers', 2200, N'211', N'2119999999'),
    (N'Operating Activities - Cash Out', 2000, N'Suppliers', 2200, N'43', N'439999'),
    (N'Operating Activities - Cash Out', 2000, N'Admin. and Selling Expenses', 2400, N'441001', N'441001'),
    (N'Operating Activities - Cash Out', 2000, N'Admin. and Selling Expenses', 2400, N'441003', N'441999'),
    (N'Operating Activities - Cash Out', 2000, N'Interest and Taxes Paid', 2500, N'442', N'4439999'),
    (N'Operating Activities - Cash Out', 2000, N'Interest and Taxes Paid', 2500, N'215', N'215999'),
    (N'Operating Activities - Cash Out', 2000, N'Interest and Taxes Paid', 2500, N'2161', N'2161999'),
    (N'Operating Activities - Cash Out', 2000, N'Other', 2600, N'445', N'449999'),
    (N'Investing Activities', 3000, N'Assets', 3000, N'11', N'119999'),
    (N'Financing Activities', 4000, N'Debt', 4000, N'221', N'221999'),
    (N'Financing Activities', 4000, N'Debt', 4000, N'222', N'229999'),
    (N'Financing Activities', 4000, N'Equity', 4100, N'213', N'213999'),
    (N'Financing Activities', 4000, N'Equity', 4100, N'214', N'214999'),
    (N'Other', 5000, N'Vat Clear A/C', 5100, N'216301', N'216301'),
    (N'Other', 5000, N'Social Insurance', 5200, N'212002', N'212002'),
    (N'Other', 5000, N'Social Insurance', 5200, N'447', N'447999'),
    (N'Other', 5000, N'Other', 5400, N'121', N'1219999'),
    (N'Other', 5000, N'Other', 5400, N'123', N'123028'),
    (N'Other', 5000, N'Other', 5400, N'123030', N'123999'),
    (N'Other', 5000, N'Other', 5400, N'212001', N'212001'),
    (N'Other', 5000, N'Other', 5400, N'212002', N'212002'),
    (N'Other', 5000, N'Other', 5400, N'212004', N'212021'),
    (N'Other', 5000, N'Other', 5400, N'212023', N'212999'),
    (N'Other', 5000, N'Other', 5400, N'217', N'219999'),
    (N'Other', 5000, N'Other', 5400, N'2164', N'2169999'),
    (N'Other', 5000, N'Other', 5400, N'125', N'125999'),
    (N'Bank', 6000, N'Cash A/C', 6000, N'124001', N'124001'),
    (N'Bank', 6000, N'Cash A/C', 6000, N'124005', N'124005'),
    (N'Bank', 6000, N'Cash A/C', 6000, N'124007', N'124007'),
    (N'Bank', 6000, N'Bank Institutes', 6100, N'124002', N'124004'),
    (N'Bank', 6000, N'Bank Institutes', 6100, N'124006', N'124006'),
    (N'Bank', 6000, N'Bank Institutes', 6100, N'124008', N'124999');
";

    public static async Task EnsureSchemaAsync(string connectionString)
    {
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(MigrationSql, conn);
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync();
            LogInfo?.Invoke($"dboReportsAI schema verified/created for {conn.Database}");
        }
        catch (Exception ex)
        {
            var db = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            LogWarning?.Invoke($"Could not ensure dboReportsAI schema on {db}", ex);
        }
    }
}
