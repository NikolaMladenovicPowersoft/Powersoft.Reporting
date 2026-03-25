-- Add AI Analysis columns to tbl_ReportSchedule
-- Run against: TENANT database

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tbl_ReportSchedule') AND name = N'IncludeAiAnalysis')
BEGIN
    ALTER TABLE tbl_ReportSchedule ADD IncludeAiAnalysis BIT NOT NULL DEFAULT 0;
    PRINT 'Added IncludeAiAnalysis to tbl_ReportSchedule';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tbl_ReportSchedule') AND name = N'AiLocale')
BEGIN
    ALTER TABLE tbl_ReportSchedule ADD AiLocale NVARCHAR(10) NOT NULL DEFAULT 'el';
    PRINT 'Added AiLocale to tbl_ReportSchedule';
END
GO
