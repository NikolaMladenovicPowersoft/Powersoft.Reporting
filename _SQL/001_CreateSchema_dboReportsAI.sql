-- =============================================================
-- Create dboReportsAI schema and all Report-AI tables
-- Run against: TENANT database (each linked to Reports AI module)
-- Idempotent: safe to run multiple times
-- =============================================================

-- 1. Create schema if not exists
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'dboReportsAI')
BEGIN
    EXEC('CREATE SCHEMA dboReportsAI');
    PRINT 'Created schema dboReportsAI';
END
GO

-- 2. tbl_ReportSchedule
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dboReportsAI.tbl_ReportSchedule') AND type = 'U')
BEGIN
    CREATE TABLE dboReportsAI.tbl_ReportSchedule (
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
    PRINT 'Created dboReportsAI.tbl_ReportSchedule';
END
GO

-- 3. tbl_ReportEmailTemplate
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dboReportsAI.tbl_ReportEmailTemplate') AND type = 'U')
BEGIN
    CREATE TABLE dboReportsAI.tbl_ReportEmailTemplate (
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

    INSERT INTO dboReportsAI.tbl_ReportEmailTemplate (TemplateName, EmailSubject, EmailBodyHtml, IsDefault, CreatedBy)
    VALUES (
        'Default Report Template',
        N'«ReportName» — «DatabaseName» («Period»)',
        N'<div style="font-family:''Segoe UI'',Arial,sans-serif;max-width:640px;margin:0 auto;color:#1f2937;">
  <div style="background:linear-gradient(135deg,#1e40af,#3b82f6);padding:24px 32px;border-radius:8px 8px 0 0;">
    <h1 style="margin:0;color:#ffffff;font-size:20px;font-weight:600;">Powersoft Reports</h1>
  </div>
  <div style="background:#ffffff;padding:28px 32px;border:1px solid #e5e7eb;border-top:none;">
    <h2 style="margin:0 0 8px;color:#1e40af;font-size:18px;">«ReportName»</h2>
    <p style="margin:0 0 20px;color:#6b7280;font-size:13px;">«DatabaseName»</p>
    <p style="margin:0 0 16px;font-size:14px;line-height:1.6;">
      Please find the attached <strong>«ReportName»</strong> report.
    </p>
    <table style="border-collapse:collapse;width:100%;margin:0 0 20px;font-size:13px;">
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;width:120px;">Period</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;font-weight:600;">«Period»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;">Rows</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;">«RowCount»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;">Format</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;">«ExportFormat»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;color:#6b7280;">Generated</td>
        <td style="padding:8px 14px;">«GeneratedDate»</td>
      </tr>
    </table>
  </div>
  <div style="background:#f9fafb;padding:16px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;">
    <p style="margin:0;color:#9ca3af;font-size:11px;">
      Automated report by Powersoft Report Engine &bull; «CompanyName»
    </p>
  </div>
</div>',
        1,
        'SYSTEM'
    );

    PRINT 'Created dboReportsAI.tbl_ReportEmailTemplate with default template';
END
ELSE
BEGIN
    UPDATE dboReportsAI.tbl_ReportEmailTemplate
    SET    EmailSubject  = N'«ReportName» — «DatabaseName» («Period»)',
           EmailBodyHtml = N'<div style="font-family:''Segoe UI'',Arial,sans-serif;max-width:640px;margin:0 auto;color:#1f2937;">
  <div style="background:linear-gradient(135deg,#1e40af,#3b82f6);padding:24px 32px;border-radius:8px 8px 0 0;">
    <h1 style="margin:0;color:#ffffff;font-size:20px;font-weight:600;">Powersoft Reports</h1>
  </div>
  <div style="background:#ffffff;padding:28px 32px;border:1px solid #e5e7eb;border-top:none;">
    <h2 style="margin:0 0 8px;color:#1e40af;font-size:18px;">«ReportName»</h2>
    <p style="margin:0 0 20px;color:#6b7280;font-size:13px;">«DatabaseName»</p>
    <p style="margin:0 0 16px;font-size:14px;line-height:1.6;">
      Please find the attached <strong>«ReportName»</strong> report.
    </p>
    <table style="border-collapse:collapse;width:100%;margin:0 0 20px;font-size:13px;">
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;width:120px;">Period</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;font-weight:600;">«Period»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;">Rows</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;">«RowCount»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;color:#6b7280;">Format</td>
        <td style="padding:8px 14px;border-bottom:1px solid #f3f4f6;">«ExportFormat»</td>
      </tr>
      <tr>
        <td style="padding:8px 14px;color:#6b7280;">Generated</td>
        <td style="padding:8px 14px;">«GeneratedDate»</td>
      </tr>
    </table>
  </div>
  <div style="background:#f9fafb;padding:16px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;">
    <p style="margin:0;color:#9ca3af;font-size:11px;">
      Automated report by Powersoft Report Engine &bull; «CompanyName»
    </p>
  </div>
</div>',
           ModifiedDate  = GETDATE(),
           ModifiedBy    = 'SYSTEM'
    WHERE  TemplateName = 'Default Report Template'
      AND  IsDefault = 1
      AND  EmailBodyHtml LIKE '%[[]ReportName]%';

    PRINT 'Updated default template to new branded version';
END
GO

-- 4. tbl_ReportScheduleLog
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dboReportsAI.tbl_ReportScheduleLog') AND type = 'U')
BEGIN
    CREATE TABLE dboReportsAI.tbl_ReportScheduleLog (
        pk_LogID        INT IDENTITY(1,1) PRIMARY KEY,
        fk_ScheduleID   INT          NOT NULL REFERENCES dboReportsAI.tbl_ReportSchedule(pk_ScheduleID),
        RunDate          DATETIME     NOT NULL DEFAULT GETDATE(),
        Status           NVARCHAR(20) NOT NULL,
        RowsGenerated    INT          NULL,
        FileSizeBytes    BIGINT       NULL,
        ErrorMessage     NVARCHAR(MAX) NULL,
        DurationMs       INT          NULL
    );

    CREATE INDEX IX_ScheduleLog_Schedule ON dboReportsAI.tbl_ReportScheduleLog(fk_ScheduleID, RunDate DESC);

    PRINT 'Created dboReportsAI.tbl_ReportScheduleLog';
END
GO

-- 5. tbl_AiPromptTemplate
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dboReportsAI.tbl_AiPromptTemplate') AND type = 'U')
BEGIN
    CREATE TABLE dboReportsAI.tbl_AiPromptTemplate (
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

    PRINT 'Created dboReportsAI.tbl_AiPromptTemplate';
END
GO

-- 6. Migrate data from old dbo tables (if they exist) and drop them
--    Uses dynamic SQL to handle old tables that may not have all columns
--    (e.g. SkipIfEmpty, RecurrenceJson, IncludeAiAnalysis, AiLocale were added by ALTER scripts)
IF OBJECT_ID(N'dbo.tbl_ReportSchedule', 'U') IS NOT NULL
BEGIN
    DECLARE @colList NVARCHAR(MAX) = 'pk_ScheduleID, ReportType, ScheduleName, CreatedBy, CreatedDate, IsActive,
        RecurrenceType, RecurrenceDay, ScheduleTime, NextRunDate, LastRunDate,
        ParametersJson, ExportFormat, Recipients, EmailSubject, ModifiedDate, ModifiedBy';
    DECLARE @valList NVARCHAR(MAX) = @colList;
    DECLARE @insertCols NVARCHAR(MAX) = @colList;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tbl_ReportSchedule') AND name = 'RecurrenceJson')
    BEGIN SET @insertCols += ', RecurrenceJson'; SET @valList += ', RecurrenceJson'; END
    ELSE
    BEGIN SET @insertCols += ', RecurrenceJson'; SET @valList += ', NULL'; END

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tbl_ReportSchedule') AND name = 'IncludeAiAnalysis')
    BEGIN SET @insertCols += ', IncludeAiAnalysis'; SET @valList += ', ISNULL(IncludeAiAnalysis, 0)'; END
    ELSE
    BEGIN SET @insertCols += ', IncludeAiAnalysis'; SET @valList += ', 0'; END

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tbl_ReportSchedule') AND name = 'AiLocale')
    BEGIN SET @insertCols += ', AiLocale'; SET @valList += ', ISNULL(AiLocale, ''el'')'; END
    ELSE
    BEGIN SET @insertCols += ', AiLocale'; SET @valList += ', ''el'''; END

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tbl_ReportSchedule') AND name = 'SkipIfEmpty')
    BEGIN SET @insertCols += ', SkipIfEmpty'; SET @valList += ', ISNULL(SkipIfEmpty, 0)'; END
    ELSE
    BEGIN SET @insertCols += ', SkipIfEmpty'; SET @valList += ', 0'; END

    DECLARE @migSql NVARCHAR(MAX) = '
        SET IDENTITY_INSERT dboReportsAI.tbl_ReportSchedule ON;
        INSERT INTO dboReportsAI.tbl_ReportSchedule (' + @insertCols + ')
        SELECT ' + @valList + '
        FROM dbo.tbl_ReportSchedule src
        WHERE NOT EXISTS (SELECT 1 FROM dboReportsAI.tbl_ReportSchedule x WHERE x.pk_ScheduleID = src.pk_ScheduleID);
        SET IDENTITY_INSERT dboReportsAI.tbl_ReportSchedule OFF;';
    EXEC sp_executesql @migSql;
    PRINT 'Migrated data from dbo.tbl_ReportSchedule';
END
GO

IF OBJECT_ID(N'dbo.tbl_ReportScheduleLog', 'U') IS NOT NULL
BEGIN
    SET IDENTITY_INSERT dboReportsAI.tbl_ReportScheduleLog ON;
    INSERT INTO dboReportsAI.tbl_ReportScheduleLog (pk_LogID, fk_ScheduleID, RunDate, Status, RowsGenerated, FileSizeBytes, ErrorMessage, DurationMs)
    SELECT pk_LogID, fk_ScheduleID, RunDate, Status, RowsGenerated, FileSizeBytes, ErrorMessage, DurationMs
    FROM dbo.tbl_ReportScheduleLog
    WHERE NOT EXISTS (SELECT 1 FROM dboReportsAI.tbl_ReportScheduleLog x WHERE x.pk_LogID = dbo.tbl_ReportScheduleLog.pk_LogID);
    SET IDENTITY_INSERT dboReportsAI.tbl_ReportScheduleLog OFF;
    PRINT 'Migrated data from dbo.tbl_ReportScheduleLog';
END
GO

IF OBJECT_ID(N'dbo.tbl_ReportEmailTemplate', 'U') IS NOT NULL
BEGIN
    SET IDENTITY_INSERT dboReportsAI.tbl_ReportEmailTemplate ON;
    INSERT INTO dboReportsAI.tbl_ReportEmailTemplate (pk_TemplateID, TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy)
    SELECT pk_TemplateID, TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy
    FROM dbo.tbl_ReportEmailTemplate
    WHERE NOT EXISTS (SELECT 1 FROM dboReportsAI.tbl_ReportEmailTemplate x WHERE x.pk_TemplateID = dbo.tbl_ReportEmailTemplate.pk_TemplateID);
    SET IDENTITY_INSERT dboReportsAI.tbl_ReportEmailTemplate OFF;
    PRINT 'Migrated data from dbo.tbl_ReportEmailTemplate';
END
GO

IF OBJECT_ID(N'dbo.tbl_AiPromptTemplate', 'U') IS NOT NULL
BEGIN
    SET IDENTITY_INSERT dboReportsAI.tbl_AiPromptTemplate ON;
    INSERT INTO dboReportsAI.tbl_AiPromptTemplate (pk_TemplateID, TemplateName, ReportType, SystemPrompt, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy)
    SELECT pk_TemplateID, TemplateName, ReportType, SystemPrompt, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy
    FROM dbo.tbl_AiPromptTemplate
    WHERE NOT EXISTS (SELECT 1 FROM dboReportsAI.tbl_AiPromptTemplate x WHERE x.pk_TemplateID = dbo.tbl_AiPromptTemplate.pk_TemplateID);
    SET IDENTITY_INSERT dboReportsAI.tbl_AiPromptTemplate OFF;
    PRINT 'Migrated data from dbo.tbl_AiPromptTemplate';
END
GO

-- 7. Drop old dbo tables (log first because of FK)
IF OBJECT_ID(N'dbo.tbl_ReportScheduleLog', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tbl_ReportScheduleLog;
    PRINT 'Dropped dbo.tbl_ReportScheduleLog';
END
GO
IF OBJECT_ID(N'dbo.tbl_ReportSchedule', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tbl_ReportSchedule;
    PRINT 'Dropped dbo.tbl_ReportSchedule';
END
GO
IF OBJECT_ID(N'dbo.tbl_ReportEmailTemplate', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tbl_ReportEmailTemplate;
    PRINT 'Dropped dbo.tbl_ReportEmailTemplate';
END
GO
IF OBJECT_ID(N'dbo.tbl_AiPromptTemplate', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tbl_AiPromptTemplate;
    PRINT 'Dropped dbo.tbl_AiPromptTemplate';
END
GO

PRINT '=== dboReportsAI schema setup complete ===';
GO
