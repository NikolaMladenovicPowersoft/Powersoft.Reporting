-- Add RecurrenceJson column for Outlook-style recurrence (Priority 4)
-- Run against: TENANT database

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'tbl_ReportSchedule') AND name = 'RecurrenceJson'
)
BEGIN
    ALTER TABLE tbl_ReportSchedule
    ADD RecurrenceJson NVARCHAR(MAX) NULL;
    PRINT 'Added RecurrenceJson to tbl_ReportSchedule';
END
GO
