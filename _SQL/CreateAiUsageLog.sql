-- Central AI usage log (psCentral). Idempotent — safe to run multiple times.
-- The app also creates this automatically at startup IF the central login has DDL rights.
-- Run this manually on psCentral if startup logs show:
--   "Could not ensure central AI usage log table — run _SQL/CreateAiUsageLog.sql manually on psCentral."

IF OBJECT_ID('dbo.tbl_RE_AiUsageLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_RE_AiUsageLog (
        pk_LogID      BIGINT IDENTITY(1,1) CONSTRAINT PK_RE_AiUsageLog PRIMARY KEY,
        DBCode        NVARCHAR(50)  NOT NULL,
        DBName        NVARCHAR(200) NULL,
        UserCode      NVARCHAR(100) NULL,
        ReportType    NVARCHAR(50)  NULL,
        InputTokens   INT           NOT NULL CONSTRAINT DF_RE_AiUsageLog_In  DEFAULT(0),
        OutputTokens  INT           NOT NULL CONSTRAINT DF_RE_AiUsageLog_Out DEFAULT(0),
        EstimatedCost DECIMAL(18,6) NOT NULL CONSTRAINT DF_RE_AiUsageLog_Est DEFAULT(0),
        ActualCost    DECIMAL(18,6) NOT NULL CONSTRAINT DF_RE_AiUsageLog_Act DEFAULT(0),
        Source        NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_AiUsageLog_Src DEFAULT('Interactive'),
        AnalysisDate  DATETIME      NOT NULL CONSTRAINT DF_RE_AiUsageLog_Dt  DEFAULT(GETDATE())
    );
    CREATE INDEX IX_RE_AiUsageLog_Date   ON dbo.tbl_RE_AiUsageLog (AnalysisDate);
    CREATE INDEX IX_RE_AiUsageLog_DBCode ON dbo.tbl_RE_AiUsageLog (DBCode);
END
GO
