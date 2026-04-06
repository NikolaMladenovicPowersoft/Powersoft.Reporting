-- Add SkipIfEmpty column to tbl_ReportSchedule
-- Run against: TENANT database

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tbl_ReportSchedule') AND name = N'SkipIfEmpty')
BEGIN
    ALTER TABLE tbl_ReportSchedule ADD SkipIfEmpty BIT NOT NULL DEFAULT 0;
    PRINT 'Added SkipIfEmpty to tbl_ReportSchedule';
END
GO
