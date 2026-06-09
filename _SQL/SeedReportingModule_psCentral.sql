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
-- Step 3b: Actions 6027–6042 (added 2026-06)
--   6027/6028: View/Schedule Purchases vs Sales
--   6029/6030: View/Schedule Pareto 80/20
--   6031/6032: View/Schedule Charts & Dashboards
--   6033/6034: View/Schedule Catalogue
--   6035/6036: View/Schedule Prospect Clients
--   6037/6038: View/Schedule Offers Report
--   6039/6040: View/Schedule Below Min Stock
--   6041/6042: View/Schedule Cancel Log
-- =====================================================

-- 6027 View Purchases vs Sales
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6027)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6027, 1085, 'ViewPurchasesSales', 'View Purchases vs Sales Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6027 AND fk_ActionCategoryID <> 1085;

-- 6028 Schedule Purchases vs Sales
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6028)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6028, 1085, 'SchedulePurchasesSales', 'Schedule Purchases vs Sales Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6028 AND fk_ActionCategoryID <> 1085;

-- 6029 View Pareto 80/20
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6029)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6029, 1085, 'ViewPareto', 'View Pareto 80/20 Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6029 AND fk_ActionCategoryID <> 1085;

-- 6030 Schedule Pareto 80/20
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6030)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6030, 1085, 'SchedulePareto', 'Schedule Pareto 80/20 Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6030 AND fk_ActionCategoryID <> 1085;

-- 6031 View Charts & Dashboards
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6031)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6031, 1085, 'ViewCharts', 'View Charts & Dashboards', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6031 AND fk_ActionCategoryID <> 1085;

-- 6032 Schedule Charts & Dashboards
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6032)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6032, 1085, 'ScheduleCharts', 'Schedule Charts & Dashboards', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6032 AND fk_ActionCategoryID <> 1085;

-- 6033 View Catalogue
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6033)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6033, 1085, 'ViewCatalogue', 'View Power Reports Catalogue', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6033 AND fk_ActionCategoryID <> 1085;

-- 6034 Schedule Catalogue
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6034)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6034, 1085, 'ScheduleCatalogue', 'Schedule Power Reports Catalogue', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6034 AND fk_ActionCategoryID <> 1085;

-- 6035 View Prospect Clients
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6035)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6035, 1085, 'ViewProspectClients', 'View Prospect Clients Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6035 AND fk_ActionCategoryID <> 1085;

-- 6036 Schedule Prospect Clients
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6036)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6036, 1085, 'ScheduleProspectClients', 'Schedule Prospect Clients Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6036 AND fk_ActionCategoryID <> 1085;

-- 6037 View Offers Report
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6037)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6037, 1085, 'ViewOffersReport', 'View Offers Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6037 AND fk_ActionCategoryID <> 1085;

-- 6038 Schedule Offers Report
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6038)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6038, 1085, 'ScheduleOffersReport', 'Schedule Offers Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6038 AND fk_ActionCategoryID <> 1085;

-- 6039 View Below Min Stock
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6039)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6039, 1085, 'ViewBelowMinStock', 'View Below Minimum Stock Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6039 AND fk_ActionCategoryID <> 1085;

-- 6040 Schedule Below Min Stock
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6040)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6040, 1085, 'ScheduleBelowMinStock', 'Schedule Below Minimum Stock Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6040 AND fk_ActionCategoryID <> 1085;

-- 6041 View Cancel Log
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6041)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6041, 1085, 'ViewCancelLog', 'View Cancel Log Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6041 AND fk_ActionCategoryID <> 1085;

-- 6042 Schedule Cancel Log
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6042)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6042, 1085, 'ScheduleCancelLog', 'Schedule Cancel Log Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6042 AND fk_ActionCategoryID <> 1085;
GO

-- Link all new actions to module RENGINEAI
DECLARE @newActions TABLE (ActionID INT);
INSERT INTO @newActions VALUES (6027),(6028),(6029),(6030),(6031),(6032),
    (6033),(6034),(6035),(6036),(6037),(6038),(6039),(6040),(6041),(6042);

INSERT INTO tbl_RelModuleAction (fk_ModuleCode, fk_ActionID)
SELECT 'RENGINEAI', a.ActionID
FROM @newActions a
WHERE NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleAction rma
    WHERE rma.fk_ModuleCode = 'RENGINEAI' AND rma.fk_ActionID = a.ActionID
);

PRINT 'Linked new actions 6027-6042 to RENGINEAI module (skipped existing).';
GO

-- =====================================================
-- Step 3c: Actions 6043/6044 (added 2026-06)
--   6043: View Trial Balance
--   6044: Schedule Trial Balance
-- =====================================================

-- 6043 View Trial Balance
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6043)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6043, 1085, 'ViewTrialBalance', 'View Trial Balance Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6043 AND fk_ActionCategoryID <> 1085;

-- 6044 Schedule Trial Balance
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6044)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6044, 1085, 'ScheduleTrialBalance', 'Schedule Trial Balance Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6044 AND fk_ActionCategoryID <> 1085;
GO

INSERT INTO tbl_RelModuleAction (fk_ModuleCode, fk_ActionID)
SELECT 'RENGINEAI', v.ActionID
FROM (VALUES (6043),(6044)) AS v(ActionID)
WHERE NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleAction rma
    WHERE rma.fk_ModuleCode = 'RENGINEAI' AND rma.fk_ActionID = v.ActionID
);

PRINT 'Linked new actions 6043-6044 (Trial Balance) to RENGINEAI module (skipped existing).';
GO

-- =====================================================
-- Step 3d: Actions 6045/6046 (added 2026-06)
--   6045: View Profit & Loss
--   6046: Schedule Profit & Loss
-- =====================================================

-- 6045 View Profit & Loss
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6045)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6045, 1085, 'ViewProfitLoss', 'View Profit & Loss Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6045 AND fk_ActionCategoryID <> 1085;

-- 6046 Schedule Profit & Loss
IF NOT EXISTS (SELECT 1 FROM tbl_Action WHERE pk_ActionID = 6046)
    INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
    VALUES (6046, 1085, 'ScheduleProfitLoss', 'Schedule Profit & Loss Report', 1, 0);
ELSE
    UPDATE tbl_Action SET fk_ActionCategoryID = 1085 WHERE pk_ActionID = 6046 AND fk_ActionCategoryID <> 1085;
GO

INSERT INTO tbl_RelModuleAction (fk_ModuleCode, fk_ActionID)
SELECT 'RENGINEAI', v.ActionID
FROM (VALUES (6045),(6046)) AS v(ActionID)
WHERE NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleAction rma
    WHERE rma.fk_ModuleCode = 'RENGINEAI' AND rma.fk_ActionID = v.ActionID
);

PRINT 'Linked new actions 6045-6046 (Profit & Loss) to RENGINEAI module (skipped existing).';
GO

-- NOTE: Actions 6015 (ViewCost) and 1200 (ViewSupplierList) are legacy PSBase actions.
-- They already exist in pscentral — DO NOT re-seed.
-- They are checked by IsActionAuthorizedAsync in the Reporting Engine at login.

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
