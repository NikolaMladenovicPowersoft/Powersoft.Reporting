-- =====================================================
-- Enable RENGINEAI module for a specific database
-- Database: pscentral
--
-- Usage: Set @DBCode below to the target database code,
--        then run this script.
--
-- Prerequisites: Run SeedReportingModule_psCentral.sql first.
-- =====================================================

USE [pscentral]
GO

-- *** CHANGE THIS to the database you want to enable ***
DECLARE @DBCode NVARCHAR(20) = 'YOURDBCODE';

-- Verify the database exists
IF NOT EXISTS (SELECT 1 FROM tbl_DB WHERE pk_DBCode = @DBCode)
BEGIN
    PRINT 'ERROR: Database code ''' + @DBCode + ''' not found in tbl_DB.';
    PRINT 'Available databases:';
    SELECT pk_DBCode, DBFriendlyName, DBActive FROM tbl_DB WHERE DBActive = 1 ORDER BY DBFriendlyName;
    RETURN;
END

-- Verify the module exists
IF NOT EXISTS (SELECT 1 FROM tbl_Module WHERE pk_ModuleCode = 'RENGINEAI')
BEGIN
    PRINT 'ERROR: Module RENGINEAI not found. Run SeedReportingModule_psCentral.sql first.';
    RETURN;
END

-- Link the module to the database
IF NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleDB 
    WHERE fk_ModuleCode = 'RENGINEAI' AND fk_DbCode = @DBCode
)
BEGIN
    INSERT INTO tbl_RelModuleDB (fk_ModuleCode, fk_DbCode)
    VALUES ('RENGINEAI', @DBCode);

    PRINT 'Enabled RENGINEAI for database: ' + @DBCode;
END
ELSE
BEGIN
    PRINT 'RENGINEAI already enabled for database: ' + @DBCode;
END

-- Show result
SELECT d.pk_DBCode, d.DBFriendlyName, m.fk_ModuleCode
FROM tbl_RelModuleDB m
INNER JOIN tbl_DB d ON m.fk_DbCode = d.pk_DBCode
WHERE m.fk_ModuleCode = 'RENGINEAI' AND m.fk_DbCode = @DBCode;
GO
