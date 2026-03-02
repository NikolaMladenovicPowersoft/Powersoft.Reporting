-- Report Schedule tables for Powersoft Reporting
-- Run against: TENANT database (each tenant has its own schedules)

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'tbl_ReportSchedule') AND type = 'U')
BEGIN
    CREATE TABLE tbl_ReportSchedule (
        pk_ScheduleID       INT IDENTITY(1,1) PRIMARY KEY,
        ReportType           NVARCHAR(100) NOT NULL,       -- e.g. 'AverageBasket'
        ScheduleName         NVARCHAR(200) NOT NULL,
        CreatedBy            NVARCHAR(50)  NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        IsActive             BIT           NOT NULL DEFAULT 1,
        
        -- Recurrence
        RecurrenceType       NVARCHAR(20)  NOT NULL,       -- 'Daily','Weekly','Monthly','Once'
        RecurrenceDay        INT           NULL,            -- Day of week (1-7) or day of month (1-31)
        ScheduleTime         TIME          NOT NULL DEFAULT '08:00',
        NextRunDate          DATETIME      NULL,
        LastRunDate          DATETIME      NULL,
        
        -- Report Parameters (stored as JSON for flexibility)
        ParametersJson       NVARCHAR(MAX) NULL,
        RecurrenceJson       NVARCHAR(MAX) NULL,            -- Outlook-style recurrence (Priority 4)
        
        -- Delivery
        ExportFormat         NVARCHAR(10)  NOT NULL DEFAULT 'Excel',  -- 'CSV','Excel','PDF'
        Recipients           NVARCHAR(MAX) NOT NULL,                   -- comma-separated emails
        EmailSubject         NVARCHAR(500) NULL,
        
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(50)  NULL
    );
    
    PRINT 'Created tbl_ReportSchedule';
END
GO

-- Email Templates for reports (similar to tbl_paraCommSettings in CloudAccounting)
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'tbl_ReportEmailTemplate') AND type = 'U')
BEGIN
    CREATE TABLE tbl_ReportEmailTemplate (
        pk_TemplateID       INT IDENTITY(1,1) PRIMARY KEY,
        TemplateName         NVARCHAR(100) NOT NULL,
        ReportType           NVARCHAR(100) NULL,           -- NULL = applies to all reports
        EmailSubject         NVARCHAR(500) NOT NULL DEFAULT '',
        EmailBodyHtml        NVARCHAR(MAX) NOT NULL DEFAULT '',
        IsDefault            BIT           NOT NULL DEFAULT 0,
        IsActive             BIT           NOT NULL DEFAULT 1,
        CreatedBy            NVARCHAR(50)  NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(50)  NULL
    );

    -- Insert default template
    INSERT INTO tbl_ReportEmailTemplate (TemplateName, EmailSubject, EmailBodyHtml, IsDefault, CreatedBy)
    VALUES (
        'Default Report Template',
        'Report: «ReportName» — «Period»',
        '<div style="font-family:Arial,sans-serif;max-width:600px;">
<h2 style="color:#2563eb;">«ReportName»</h2>
<p>Dear «RecipientName»,</p>
<p>Please find attached the <strong>«ReportName»</strong> report for <strong>«DatabaseName»</strong>.</p>
<table style="border-collapse:collapse;width:100%;margin:16px 0;">
<tr><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;">Period</td><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;"><strong>«Period»</strong></td></tr>
<tr><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;">Rows</td><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;">«RowCount»</td></tr>
<tr><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;">Format</td><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;">«ExportFormat»</td></tr>
<tr><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;">Generated</td><td style="padding:6px 12px;border-bottom:1px solid #e5e7eb;">«GeneratedDate»</td></tr>
</table>
<p style="color:#9ca3af;font-size:11px;margin-top:24px;">This is an automated report from Powersoft Reporting Engine.</p>
</div>',
        1,
        'SYSTEM'
    );

    PRINT 'Created tbl_ReportEmailTemplate with default template';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'tbl_ReportScheduleLog') AND type = 'U')
BEGIN
    CREATE TABLE tbl_ReportScheduleLog (
        pk_LogID        INT IDENTITY(1,1) PRIMARY KEY,
        fk_ScheduleID   INT          NOT NULL REFERENCES tbl_ReportSchedule(pk_ScheduleID),
        RunDate          DATETIME     NOT NULL DEFAULT GETDATE(),
        Status           NVARCHAR(20) NOT NULL,  -- 'Success','Failed','Skipped'
        RowsGenerated    INT          NULL,
        FileSizeBytes    BIGINT       NULL,
        ErrorMessage     NVARCHAR(MAX) NULL,
        DurationMs       INT          NULL
    );
    
    CREATE INDEX IX_ScheduleLog_Schedule ON tbl_ReportScheduleLog(fk_ScheduleID, RunDate DESC);
    
    PRINT 'Created tbl_ReportScheduleLog';
END
GO
