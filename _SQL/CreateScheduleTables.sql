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
