-- =====================================================
-- Create two permission test users for P0 testing
-- Database: pscentral
-- Run on ALL servers — IDEMPOTENT (safe to run multiple times)
--
-- User 1: RTEST_ADMIN   — full access  (Ranking 15)
-- User 2: RTEST_NOCOST  — custom role, NO ViewCost, NO ViewSupplier
--
-- Password for both: Test1234
-- SHA1(UTF8) hash: DDDD5D7B474D2C78EBBB833789C4BFD721EDF4BF
--
-- NOTE: tbl_Role.pk_RoleID has NO IDENTITY — we use high IDs (9980/9981)
--       that are very unlikely to clash with existing roles.
--       Adjust if your environment already has roles in the 9900+ range.
-- =====================================================

USE [pscentral]
GO

SET NOCOUNT ON;

-- =====================================================
-- Step 1: Roles
-- =====================================================

-- Role 9980: RTEST_ADMIN_ROLE — Ranking 15 = all actions allowed
IF NOT EXISTS (SELECT 1 FROM tbl_Role WHERE pk_RoleID = 9980)
BEGIN
    INSERT INTO tbl_Role (pk_RoleID, RoleName, Ranking, RoleActive)
    VALUES (9980, 'RTEST_ADMIN_ROLE', 15, 1);
    PRINT 'Created role 9980: RTEST_ADMIN_ROLE (Ranking 15 = full access)';
END
ELSE
BEGIN
    UPDATE tbl_Role SET RoleName = 'RTEST_ADMIN_ROLE', Ranking = 15, RoleActive = 1
    WHERE pk_RoleID = 9980;
    PRINT 'Role 9980 already exists — verified.';
END

-- Role 9981: RTEST_NOCOST_ROLE — Ranking 25 = custom, will be checked per-action
IF NOT EXISTS (SELECT 1 FROM tbl_Role WHERE pk_RoleID = 9981)
BEGIN
    INSERT INTO tbl_Role (pk_RoleID, RoleName, Ranking, RoleActive)
    VALUES (9981, 'RTEST_NOCOST_ROLE', 25, 1);
    PRINT 'Created role 9981: RTEST_NOCOST_ROLE (Ranking 25 = custom)';
END
ELSE
BEGIN
    UPDATE tbl_Role SET RoleName = 'RTEST_NOCOST_ROLE', Ranking = 25, RoleActive = 1
    WHERE pk_RoleID = 9981;
    PRINT 'Role 9981 already exists — verified.';
END
GO

-- =====================================================
-- Step 2: Grant actions to RTEST_NOCOST_ROLE (9981)
-- Has:     View AB (6025), View Catalogue (6033),
--          View ProspectClients (6035), View OffersReport (6037)
-- Missing: 6015 (ViewCost), 1200 (ViewSupplier),
--          6027 (ViewPS), 6029 (ViewPareto), 6031 (ViewCharts),
--          6039 (ViewBMS), 6041 (ViewCancelLog)
--          + no schedule rights at all
-- =====================================================

DECLARE @NoCostRoleID INT = 9981;

-- Remove any leftover actions from previous runs (keep idempotent)
DELETE FROM tbl_RelRoleAction WHERE fk_RoleID = @NoCostRoleID;

-- Grant only the 4 basic view actions
INSERT INTO tbl_RelRoleAction (fk_RoleID, fk_ActionID)
SELECT @NoCostRoleID, a
FROM (VALUES (6025), (6033), (6035), (6037)) AS t(a)
WHERE NOT EXISTS (
    SELECT 1 FROM tbl_RelRoleAction
    WHERE fk_RoleID = @NoCostRoleID AND fk_ActionID = t.a
);

PRINT 'RTEST_NOCOST_ROLE: granted actions 6025, 6033, 6035, 6037';
PRINT '  NO ViewCost (6015), NO ViewSupplier (1200), NO Purchases/Pareto/Charts/BMS/CancelLog';
GO

-- =====================================================
-- Step 3: Users
-- =====================================================

DECLARE @PasswordHash NVARCHAR(255) = 'DDDD5D7B474D2C78EBBB833789C4BFD721EDF4BF';

DECLARE @CompanyCode NVARCHAR(20);
SELECT TOP 1 @CompanyCode = pk_CompanyCode
FROM tbl_Company WHERE CompanyActive = 1
ORDER BY pk_CompanyCode;

-- USER 1: RTEST_ADMIN — Ranking 15, all actions allowed automatically
IF NOT EXISTS (SELECT 1 FROM tbl_User WHERE pk_UserCode = 'RTEST_ADMIN')
BEGIN
    INSERT INTO tbl_User (
        pk_UserCode, UserDesc, UserPassword, UserEmail,
        CompAdmin, UserActive, fk_CompanyCode, fk_RoleID,
        UserAllStores, UserBilling, UserVersion, UserUpdate,
        CEO, PowersoftSupport, BR, OTP, EmailAuthorization
    ) VALUES (
        'RTEST_ADMIN',
        'Reporting Test - Full Access',
        @PasswordHash,
        'rtest_admin@test.com',
        0, 1, @CompanyCode, 9980,   -- Role 9980 = Ranking 15
        1, 0, 0, 0,
        0, 0, 0, 0, 0
    );
    PRINT 'Created user: RTEST_ADMIN / Test1234 (full access)';
END
ELSE
BEGIN
    UPDATE tbl_User
    SET fk_RoleID = 9980, UserActive = 1, UserPassword = @PasswordHash
    WHERE pk_UserCode = 'RTEST_ADMIN';
    PRINT 'User RTEST_ADMIN already exists — role + password re-verified.';
END

-- USER 2: RTEST_NOCOST — Ranking 25, no cost/supplier rights
IF NOT EXISTS (SELECT 1 FROM tbl_User WHERE pk_UserCode = 'RTEST_NOCOST')
BEGIN
    INSERT INTO tbl_User (
        pk_UserCode, UserDesc, UserPassword, UserEmail,
        CompAdmin, UserActive, fk_CompanyCode, fk_RoleID,
        UserAllStores, UserBilling, UserVersion, UserUpdate,
        CEO, PowersoftSupport, BR, OTP, EmailAuthorization
    ) VALUES (
        'RTEST_NOCOST',
        'Reporting Test - No Cost/Supplier',
        @PasswordHash,
        'rtest_nocost@test.com',
        0, 1, @CompanyCode, 9981,   -- Role 9981 = Ranking 25
        1, 0, 0, 0,
        0, 0, 0, 0, 0
    );
    PRINT 'Created user: RTEST_NOCOST / Test1234 (no cost/supplier)';
END
ELSE
BEGIN
    UPDATE tbl_User
    SET fk_RoleID = 9981, UserActive = 1, UserPassword = @PasswordHash
    WHERE pk_UserCode = 'RTEST_NOCOST';
    PRINT 'User RTEST_NOCOST already exists — role + password re-verified.';
END
GO

-- =====================================================
-- Step 4: Enable RENGINEAI module for the company's database
-- (needed so users can select a DB after login)
-- =====================================================

DECLARE @CompanyCode NVARCHAR(20);
SELECT TOP 1 @CompanyCode = pk_CompanyCode
FROM tbl_Company WHERE CompanyActive = 1
ORDER BY pk_CompanyCode;

DECLARE @DBCode NVARCHAR(20);
SELECT TOP 1 @DBCode = d.pk_DBCode
FROM tbl_DB d
WHERE d.fk_CompanyCode = @CompanyCode

INSERT INTO tbl_RelModuleDb (fk_ModuleCode, fk_DBCode)
SELECT 'RENGINEAI', @DBCode
WHERE @DBCode IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM tbl_RelModuleDb
    WHERE fk_ModuleCode = 'RENGINEAI' AND fk_DBCode = @DBCode
);

PRINT 'Module RENGINEAI linked to DB: ' + ISNULL(@DBCode, 'NOT FOUND — run EnableModuleForDatabase manually');
GO

-- =====================================================
-- Step 5: Link both users to the DB (tbl_RelUserDB)
-- Only needed if your system uses per-user DB filtering
-- =====================================================

DECLARE @CompanyCode NVARCHAR(20);
SELECT TOP 1 @CompanyCode = pk_CompanyCode
FROM tbl_Company WHERE CompanyActive = 1 ORDER BY pk_CompanyCode;

DECLARE @DBCode NVARCHAR(20);
SELECT TOP 1 @DBCode = pk_DBCode FROM tbl_DB
WHERE fk_CompanyCode = @CompanyCode;

IF @DBCode IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tbl_RelUserDB WHERE fk_UserCode = 'RTEST_ADMIN' AND fk_DBCode = @DBCode)
        INSERT INTO tbl_RelUserDB (fk_UserCode, fk_DBCode) VALUES ('RTEST_ADMIN', @DBCode);

    IF NOT EXISTS (SELECT 1 FROM tbl_RelUserDB WHERE fk_UserCode = 'RTEST_NOCOST' AND fk_DBCode = @DBCode)
        INSERT INTO tbl_RelUserDB (fk_UserCode, fk_DBCode) VALUES ('RTEST_NOCOST', @DBCode);

    PRINT 'Users linked to DB: ' + @DBCode;
END
ELSE
    PRINT 'WARNING: No DB found for company. Run tbl_RelUserDB insert manually.';
GO

-- =====================================================
-- Verification
-- =====================================================
PRINT '';
PRINT '=== VERIFICATION ===';

SELECT u.pk_UserCode, u.UserDesc, u.UserActive,
       r.pk_RoleID, r.RoleName, r.Ranking,
       u.fk_CompanyCode
FROM tbl_User u
INNER JOIN tbl_Role r ON u.fk_RoleID = r.pk_RoleID
WHERE u.pk_UserCode IN ('RTEST_ADMIN', 'RTEST_NOCOST');

PRINT '--- Actions for RTEST_NOCOST_ROLE (9981) ---';
SELECT rra.fk_ActionID, a.ActionDesc
FROM tbl_RelRoleAction rra
INNER JOIN tbl_Action a ON rra.fk_ActionID = a.pk_ActionID
WHERE rra.fk_RoleID = 9981
ORDER BY rra.fk_ActionID;

PRINT '';
PRINT '=== DONE ===';
PRINT 'Test credentials (password: Test1234):';
PRINT '  RTEST_ADMIN  — Ranking 15 — ALL actions allowed (no per-action check)';
PRINT '  RTEST_NOCOST — Ranking 25 — ViewAB + ViewCatalogue + ViewPC + ViewOffers ONLY';
PRINT '                              NO ViewCost, NO ViewSupplier, NO PS/Pareto/Charts/BMS/CancelLog';
GO
