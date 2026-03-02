-- =====================================================
-- Seed Report Engine module, category, and actions
-- Database: pscentral (run on ALL servers)
-- 
-- This script is IDEMPOTENT — safe to run multiple times.
-- It will skip inserts if records already exist.
--
-- What it creates:
--   1. Module: RENGINEAI (Report Engine AI)
--   2. Action Category 1085 (Report Engine)
--   3. Actions: 6025 (View Average Basket), 6026 (Schedule Average Basket)
--   4. Module <-> Action mappings (tbl_RelModuleAction)
--
-- NOTE: Category 1085 per Christina's convention. Values > 9000 are reserved for admin.
--
-- Schema verified against live pscentral (2026-02-26):
--   tbl_Module:         pk_ModuleCode(nvarchar10), ModuleDesc(nvarchar100), ModuleComments(nvarchar1000), ModuleActive(bit), ModuleOrder(smallint)
--   tbl_ActionCategory: pk_ActionCategoryID(int), ActionCategoryName(nvarchar50), PowersoftSupport(bit)
--   tbl_Action:         pk_ActionID(int), fk_ActionCategoryID(int), ActionName(nvarchar100), ActionDesc(nvarchar500), PowersoftSupport(bit), AllowCEO(bit)
--   NOTE: None of the PKs use IDENTITY — all are plain int/nvarchar PKs.
-- =====================================================

USE [pscentral]
GO

SET NOCOUNT ON;

-- =====================================================
-- Step 1: Module RENGINEAI
-- =====================================================
IF NOT EXISTS (SELECT 1 FROM tbl_Module WHERE pk_ModuleCode = 'RENGINEAI')
BEGIN
    INSERT INTO tbl_Module (pk_ModuleCode, ModuleDesc, ModuleActive, ModuleOrder)
    VALUES ('RENGINEAI', 'Report Engine', 1, 100);

    PRINT 'Created module: RENGINEAI (Report Engine)';
END
ELSE
BEGIN
    UPDATE tbl_Module SET ModuleDesc = 'Report Engine' WHERE pk_ModuleCode = 'RENGINEAI' AND (ModuleDesc IS NULL OR ModuleDesc = '');
    PRINT 'Module RENGINEAI already exists — verified.';
END
GO

-- =====================================================
-- Step 2: Action Category 1085
-- =====================================================
IF NOT EXISTS (SELECT 1 FROM tbl_ActionCategory WHERE pk_ActionCategoryID = 1085)
BEGIN
    INSERT INTO tbl_ActionCategory (pk_ActionCategoryID, ActionCategoryName, PowersoftSupport)
    VALUES (1085, 'Report Engine', 1);

    PRINT 'Created action category: 1085 (Report Engine)';
END
ELSE
BEGIN
    PRINT 'Action category 1085 already exists — skipped.';
END
GO

-- =====================================================
-- Step 3: Actions 6025, 6026 (linked to category 1085)
-- =====================================================

-- Action 6025: View Average Basket report
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6025)
BEGIN
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6025, 1085, 'ViewAverageBasket', 'View Average Basket Report', 1, 0);

    PRINT 'Created action: 6025 (View Average Basket Report)';
END
ELSE
BEGIN
    -- Fix category if previously inserted with wrong value
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6025 AND fk_ActionCategoryID <> 1085;
    PRINT 'Action 6025 already exists — category verified as 1085.';
END

-- Action 6026: Schedule Average Basket report
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6026)
BEGIN
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6026, 1085, 'ScheduleAverageBasket', 'Schedule Average Basket Report', 1, 0);

    PRINT 'Created action: 6026 (Schedule Average Basket Report)';
END
ELSE
BEGIN
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6026 AND fk_ActionCategoryID <> 1085;
    PRINT 'Action 6026 already exists — category verified as 1085.';
END
GO

-- =====================================================
-- Step 4: Link Actions to Module (tbl_RelModuleAction)
-- =====================================================

IF NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleAction 
    WHERE fk_ModuleCode = 'RENGINEAI' AND fk_ActionID = 6025
)
BEGIN
    INSERT INTO tbl_RelModuleAction (fk_ModuleCode, fk_ActionID)
    VALUES ('RENGINEAI', 6025);
    PRINT 'Linked action 6025 to module RENGINEAI';
END
ELSE
    PRINT 'Action 6025 already linked to RENGINEAI — skipped.';

IF NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleAction 
    WHERE fk_ModuleCode = 'RENGINEAI' AND fk_ActionID = 6026
)
BEGIN
    INSERT INTO tbl_RelModuleAction (fk_ModuleCode, fk_ActionID)
    VALUES ('RENGINEAI', 6026);
    PRINT 'Linked action 6026 to module RENGINEAI';
END
ELSE
    PRINT 'Action 6026 already linked to RENGINEAI — skipped.';
GO

-- =====================================================
-- Cleanup: remove old category 9900 if it was created by mistake
-- =====================================================
IF EXISTS (SELECT 1 FROM tbl_ActionCategory WHERE pk_ActionCategoryID = 9900 AND ActionCategoryName = 'Report Engine')
BEGIN
    -- Only delete if no other actions reference it
    IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE fk_ActionCategoryID = 9900)
    BEGIN
        DELETE FROM tbl_ActionCategory WHERE pk_ActionCategoryID = 9900;
        PRINT 'Cleaned up old category 9900 (Report Engine) — no longer needed.';
    END
    ELSE
        PRINT 'Category 9900 still has linked actions — skipped cleanup.';
END
GO

-- =====================================================
-- Verification
-- =====================================================
PRINT '';
PRINT '=== VERIFICATION ===';

SELECT pk_ModuleCode, ModuleDesc, ModuleActive, ModuleOrder
FROM tbl_Module WHERE pk_ModuleCode = 'RENGINEAI';

SELECT pk_ActionCategoryID, ActionCategoryName, PowersoftSupport
FROM tbl_ActionCategory WHERE pk_ActionCategoryID = 1085;

SELECT pk_ActionID, ActionName, ActionDesc, fk_ActionCategoryID, PowersoftSupport, AllowCEO
FROM tbl_Action WHERE pk_ActionID IN (6025, 6026);

SELECT ma.fk_ModuleCode, ma.fk_ActionID, a.ActionDesc
FROM tbl_RelModuleAction ma
INNER JOIN tbl_Action a ON ma.fk_ActionID = a.pk_ActionID
WHERE ma.fk_ModuleCode = 'RENGINEAI';

PRINT '=== DONE ===';
GO
