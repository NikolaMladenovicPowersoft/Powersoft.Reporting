-- AI Prompt Templates for customizable AI analysis prompts
-- Run against: TENANT database

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'tbl_AiPromptTemplate') AND type = 'U')
BEGIN
    CREATE TABLE tbl_AiPromptTemplate (
        pk_TemplateID       INT IDENTITY(1,1) PRIMARY KEY,
        TemplateName         NVARCHAR(200) NOT NULL,
        ReportType           NVARCHAR(100) NULL,           -- NULL = applies to all reports
        SystemPrompt         NVARCHAR(MAX) NOT NULL,
        IsDefault            BIT           NOT NULL DEFAULT 0,
        IsActive             BIT           NOT NULL DEFAULT 1,
        CreatedBy            NVARCHAR(100) NOT NULL,
        CreatedDate          DATETIME      NOT NULL DEFAULT GETDATE(),
        ModifiedDate         DATETIME      NULL,
        ModifiedBy           NVARCHAR(100) NULL
    );

    PRINT 'Created tbl_AiPromptTemplate';
END
GO
