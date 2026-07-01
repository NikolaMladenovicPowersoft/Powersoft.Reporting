-- Central industry template packs (psCentral). Idempotent — safe to run multiple times.
-- The app also creates these automatically at startup IF the central login has DDL rights.
-- Run this manually on psCentral if startup logs show:
--   "Could not ensure central template pack tables — run _SQL/CreateTemplatePacks.sql manually on psCentral."

IF OBJECT_ID('dbo.tbl_RE_TemplatePack', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_RE_TemplatePack (
        pk_PackID     INT IDENTITY(1,1) CONSTRAINT PK_RE_TemplatePack PRIMARY KEY,
        PackCode      NVARCHAR(50)  NOT NULL CONSTRAINT UQ_RE_TemplatePack_Code UNIQUE,
        PackName      NVARCHAR(200) NOT NULL,
        IndustryTag   NVARCHAR(100) NULL,
        Description   NVARCHAR(500) NULL,
        SortOrder     INT NOT NULL CONSTRAINT DF_RE_TP_Sort   DEFAULT(0),
        IsActive      BIT NOT NULL CONSTRAINT DF_RE_TP_Active DEFAULT(1),
        CreatedBy     NVARCHAR(100) NULL,
        CreatedDate   DATETIME NOT NULL CONSTRAINT DF_RE_TP_Created DEFAULT(GETDATE()),
        ModifiedBy    NVARCHAR(100) NULL,
        ModifiedDate  DATETIME NULL
    );
END
GO

IF OBJECT_ID('dbo.tbl_RE_TemplatePackItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_RE_TemplatePackItem (
        pk_ItemID         INT IDENTITY(1,1) CONSTRAINT PK_RE_TemplatePackItem PRIMARY KEY,
        fk_PackID         INT NOT NULL CONSTRAINT FK_RE_TPItem_Pack
                              REFERENCES dbo.tbl_RE_TemplatePack(pk_PackID) ON DELETE CASCADE,
        ReportType        NVARCHAR(50)  NOT NULL,
        TemplateName      NVARCHAR(200) NOT NULL,
        ParametersJson    NVARCHAR(MAX) NULL,
        RecurrenceType    NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_TPItem_Rec  DEFAULT('Monthly'),
        RecurrenceDay     INT NULL,
        ScheduleTimeMin   INT NOT NULL CONSTRAINT DF_RE_TPItem_Time DEFAULT(480),  -- 08:00 as minutes from midnight
        ExportFormat      NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_TPItem_Fmt  DEFAULT('Excel'),
        IncludeAiAnalysis BIT NOT NULL CONSTRAINT DF_RE_TPItem_Ai   DEFAULT(0),
        AiLocale          NVARCHAR(10)  NOT NULL CONSTRAINT DF_RE_TPItem_Loc  DEFAULT('en'),
        SkipIfEmpty       BIT NOT NULL CONSTRAINT DF_RE_TPItem_Skip DEFAULT(1),
        SortOrder         INT NOT NULL CONSTRAINT DF_RE_TPItem_Sort DEFAULT(0)
    );
    CREATE INDEX IX_RE_TPItem_Pack ON dbo.tbl_RE_TemplatePackItem (fk_PackID);
END
GO
