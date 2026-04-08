using Microsoft.Data.SqlClient;

namespace Powersoft.Reporting.Data.Tenant;

public class SchemaMigrationService
{
    public static Action<string>? LogInfo { get; set; }
    public static Action<string, Exception>? LogWarning { get; set; }

    private const string SchemaName = "dboReportsAI";

    private const string DefaultSubject = "\u00ABReportName\u00BB \u2014 \u00ABDatabaseName\u00BB (\u00ABPeriod\u00BB)";

    private const string DefaultBody = @"<div style=""font-family:'Segoe UI',Arial,sans-serif;max-width:640px;margin:0 auto;color:#1f2937;"">
  <div style=""background:linear-gradient(135deg,#1e40af,#3b82f6);padding:24px 32px;border-radius:8px 8px 0 0;"">
    <h1 style=""margin:0;color:#ffffff;font-size:20px;font-weight:600;"">Powersoft Reports</h1>
  </div>
  <div style=""background:#ffffff;padding:28px 32px;border:1px solid #e5e7eb;border-top:none;"">
    <h2 style=""margin:0 0 8px;color:#1e40af;font-size:18px;"">&#171;ReportName&#187;</h2>
    <p style=""margin:0 0 20px;color:#6b7280;font-size:13px;"">&#171;DatabaseName&#187;</p>
    <p style=""margin:0 0 16px;font-size:14px;line-height:1.6;"">
      Please find the attached <strong>&#171;ReportName&#187;</strong> report.
    </p>
    <table style=""border-collapse:collapse;width:100%;margin:0 0 20px;font-size:13px;"">
      <tr>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;width:120px;"">Period</td>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;font-weight:600;"">&#171;Period&#187;</td>
      </tr>
      <tr>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;"">Rows</td>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;"">&#171;RowCount&#187;</td>
      </tr>
      <tr>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;"">Format</td>
        <td style=""padding:8px 14px;border-bottom:1px solid #f3f4f6;"">&#171;ExportFormat&#187;</td>
      </tr>
      <tr>
        <td style=""padding:8px 14px;color:#6b7280;"">Generated</td>
        <td style=""padding:8px 14px;"">&#171;GeneratedDate&#187;</td>
      </tr>
    </table>
  </div>
  <div style=""background:#f9fafb;padding:16px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;"">
    <p style=""margin:0;color:#9ca3af;font-size:11px;"">
      Automated report by Powersoft Report Engine &bull; &#171;CompanyName&#187;
    </p>
  </div>
</div>";

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
