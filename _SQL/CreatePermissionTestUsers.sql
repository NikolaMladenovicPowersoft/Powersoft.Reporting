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
-- =====================================================

USE [pscentral]
GO

SET NOCOUNT ON;

-- =====================================================
-- Step 1: Roles
-- tbl_Role.pk_RoleID has IDENTITY — must use IDENTITY_INSERT
-- We use high IDs (9980/9981) unlikely to clash
-- =====================================================

SET IDENTITY_INSERT tbl_Role ON;

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

SET IDENTITY_INSERT tbl_Role OFF;
GO

-- =====================================================
-- Step 2: Actions for RTEST_NOCOST_ROLE (9981)
-- Has:     View AB (6025), View Catalogue (6033),
--          View ProspectClients (6035), View OffersReport (6037)
-- NO:      6015 (ViewCost), 1200 (ViewSupplier),
--          6027 (ViewPS), 6029 (ViewPareto), 6031 (ViewCharts),
--          6039 (ViewBMS), 6041 (ViewCancelLog), any schedule rights
-- =====================================================

-- Clean previous run
DELETE FROM tbl_RelRoleAction WHERE fk_RoleID = 9981;

INSERT INTO tbl_RelRoleAction (fk_RoleID, fk_ActionID)
VALUES
    (9981, 6025),   -- View Average Basket
    (9981, 6033),   -- View Catalogue
    (9981, 6035),   -- View Prospect Clients
    (9981, 6037);   -- View Offers Report

PRINT 'RTEST_NOCOST_ROLE (9981): granted 4 actions (no cost/supplier/schedules)';
GO

-- =====================================================
-- Step 3: Users
-- =====================================================

DECLARE @PasswordHash NVARCHAR(255) = 'DDDD5D7B474D2C78EBBB833789C4BFD721EDF4BF';

DECLARE @CompanyCode NVARCHAR(20);
SELECT TOP 1 @CompanyCode = pk_CompanyCode
FROM tbl_Company WHERE CompanyActive = 1
ORDER BY pk_CompanyCode;

PRINT 'Using company: ' + ISNULL(@CompanyCode, 'NULL — check tbl_Company');

-- USER 1: RTEST_ADMIN
IF NOT EXISTS (SELECT 1 FROM tbl_User WHERE pk_UserCode = 'RTEST_ADMIN')
BEGIN
    INSERT INTO tbl_User (
        pk_UserCode, UserDesc, UserPassword, UserEmail,
        CompAdmin, UserActive, fk_CompanyCode, fk_RoleID,
        UserAllStores, UserBilling, UserVersion, UserUpdate,
        CEO, PowersoftSupport, BR, OTP, EmailAuthorization
    ) VALUES (
        'RTEST_ADMIN', 'Reporting Test - Full Access', @PasswordHash, 'rtest_admin@test.com',
        0, 1, @CompanyCode, 9980,
        1, 0, 0, 0, 0, 0, 0, 0, 0
    );
    PRINT 'Created user: RTEST_ADMIN / Test1234 (full access, Ranking 15)';
END
ELSE
BEGIN
    UPDATE tbl_User
    SET fk_RoleID = 9980, UserActive = 1, UserPassword = @PasswordHash
    WHERE pk_UserCode = 'RTEST_ADMIN';
    PRINT 'User RTEST_ADMIN already exists — role + password re-synced.';
END

-- USER 2: RTEST_NOCOST
IF NOT EXISTS (SELECT 1 FROM tbl_User WHERE pk_UserCode = 'RTEST_NOCOST')
BEGIN
    INSERT INTO tbl_User (
        pk_UserCode, UserDesc, UserPassword, UserEmail,
        CompAdmin, UserActive, fk_CompanyCode, fk_RoleID,
        UserAllStores, UserBilling, UserVersion, UserUpdate,
        CEO, PowersoftSupport, BR, OTP, EmailAuthorization
    ) VALUES (
        'RTEST_NOCOST', 'Reporting Test - No Cost/Supplier', @PasswordHash, 'rtest_nocost@test.com',
        0, 1, @CompanyCode, 9981,
        1, 0, 0, 0, 0, 0, 0, 0, 0
    );
    PRINT 'Created user: RTEST_NOCOST / Test1234 (no cost/supplier, Ranking 25)';
END
ELSE
BEGIN
    UPDATE tbl_User
    SET fk_RoleID = 9981, UserActive = 1, UserPassword = @PasswordHash
    WHERE pk_UserCode = 'RTEST_NOCOST';
    PRINT 'User RTEST_NOCOST already exists — role + password re-synced.';
END
GO

-- =====================================================
-- Step 4: Enable RENGINEAI module for company DB + link users
-- =====================================================

DECLARE @CompanyCode NVARCHAR(20);
SELECT TOP 1 @CompanyCode = pk_CompanyCode
FROM tbl_Company WHERE CompanyActive = 1 ORDER BY pk_CompanyCode;

DECLARE @DBCode NVARCHAR(20);
SELECT TOP 1 @DBCode = pk_DBCode FROM tbl_DB
WHERE fk_CompanyCode = @CompanyCode;

IF @DBCode IS NULL
BEGIN
    PRINT 'WARNING: No DB found for company. Link users manually via tbl_RelUserDB.';
    RETURN;
END

PRINT 'Using DB: ' + @DBCode;

-- Module -> DB link
IF NOT EXISTS (SELECT 1 FROM tbl_RelModuleDb WHERE fk_ModuleCode = 'RENGINEAI' AND fk_DBCode = @DBCode)
BEGIN
    INSERT INTO tbl_RelModuleDb (fk_ModuleCode, fk_DBCode) VALUES ('RENGINEAI', @DBCode);
    PRINT 'Linked RENGINEAI to DB: ' + @DBCode;
END
ELSE
    PRINT 'RENGINEAI already linked to DB: ' + @DBCode;

-- User -> DB links
IF NOT EXISTS (SELECT 1 FROM tbl_RelUserDB WHERE fk_UserCode = 'RTEST_ADMIN' AND fk_DBCode = @DBCode)
BEGIN
    INSERT INTO tbl_RelUserDB (fk_UserCode, fk_DBCode) VALUES ('RTEST_ADMIN', @DBCode);
    PRINT 'Linked RTEST_ADMIN to DB: ' + @DBCode;
END

IF NOT EXISTS (SELECT 1 FROM tbl_RelUserDB WHERE fk_UserCode = 'RTEST_NOCOST' AND fk_DBCode = @DBCode)
BEGIN
    INSERT INTO tbl_RelUserDB (fk_UserCode, fk_DBCode) VALUES ('RTEST_NOCOST', @DBCode);
    PRINT 'Linked RTEST_NOCOST to DB: ' + @DBCode;
END
GO

-- =====================================================
-- Verification
-- =====================================================
PRINT '';
PRINT '=== VERIFICATION ===';

SELECT u.pk_UserCode, u.UserDesc, u.UserActive,
       r.pk_RoleID, r.RoleName, r.Ranking, u.fk_CompanyCode
FROM tbl_User u
INNER JOIN tbl_Role r ON u.fk_RoleID = r.pk_RoleID
WHERE u.pk_UserCode IN ('RTEST_ADMIN', 'RTEST_NOCOST');

PRINT '--- Actions for RTEST_NOCOST_ROLE (9981) ---';
SELECT rra.fk_ActionID, a.ActionDesc
FROM tbl_RelRoleAction rra
INNER JOIN tbl_Action a ON rra.fk_ActionID = a.pk_ActionID
WHERE rra.fk_RoleID = 9981
ORDER BY rra.fk_ActionID;

PRINT '--- DB access ---';
SELECT fk_UserCode, fk_DBCode FROM tbl_RelUserDB
WHERE fk_UserCode IN ('RTEST_ADMIN', 'RTEST_NOCOST');

PRINT '';
PRINT '=== DONE ===';
PRINT 'Login with: RTEST_ADMIN / Test1234  -> full access';
PRINT 'Login with: RTEST_NOCOST / Test1234  -> no ViewCost, no ViewSupplier';
GO
