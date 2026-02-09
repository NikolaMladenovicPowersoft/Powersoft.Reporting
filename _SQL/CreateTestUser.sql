-- =====================================================
-- Create test user in PS Central (tbl_User)
-- Database: pscentral
-- =====================================================
-- Password: Test123!
-- SHA1 hash generated for UTF-16 (matches .NET legacy)
-- =====================================================

USE [pscentral]
GO

-- Get first available Role (e.g. client user role)
DECLARE @RoleID INT
SELECT TOP 1 @RoleID = pk_RoleID FROM tbl_Role ORDER BY pk_RoleID

-- Get first available Company (optional, can be NULL)
DECLARE @CompanyCode NVARCHAR(20)
SELECT TOP 1 @CompanyCode = pk_CompanyCode FROM tbl_Company WHERE CompanyActive = 1

-- Password: Test123!
-- SHA1 hash from C# Encoding.UTF8 (matches AuthenticationService)
DECLARE @PasswordHash NVARCHAR(255)
SET @PasswordHash = '0C6BA03885F3AAE765FBF20F07F514A44DBDA30A'

-- Insert test user
INSERT INTO [dbo].[tbl_User] (
    [pk_UserCode],
    [UserDesc],
    [UserPassword],
    [UserEmail],
    [CompAdmin],
    [UserActive],
    [fk_CompanyCode],
    [fk_RoleID],
    [UserAllStores],
    [UserBilling],
    [UserVersion],
    [UserUpdate],
    [CEO],
    [PowersoftSupport],
    [BR],
    [OTP],
    [EmailAuthorization]
) VALUES (
    'REPORTING_TEST',           -- pk_UserCode (username)
    'Reporting Test User',      -- UserDesc
    @PasswordHash,              -- UserPassword (SHA1)
    'test@test.com',            -- UserEmail
    0,                          -- CompAdmin
    1,                          -- UserActive
    @CompanyCode,               -- fk_CompanyCode
    ISNULL(@RoleID, 1),         -- fk_RoleID
    1,                          -- UserAllStores
    0,                          -- UserBilling
    0,                          -- UserVersion
    0,                          -- UserUpdate
    0,                          -- CEO
    0,                          -- PowersoftSupport
    0,                          -- BR
    0,                          -- OTP (disable for easy testing)
    0                           -- EmailAuthorization
)

PRINT 'Test user created:'
PRINT '  Username: REPORTING_TEST'
PRINT '  Password: Test123!'
PRINT '  Company: ' + ISNULL(@CompanyCode, 'NULL')
PRINT '  RoleID: ' + CAST(ISNULL(@RoleID, 1) AS VARCHAR(10))
GO
