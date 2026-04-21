-- =============================================================================
-- INDEXES_CATALOGUE.sql
--
-- Index proposals to support the Power Reports Catalogue under heavy load.
-- Generated 2026-04-17 — see _DOCS/CATALOGUE_PRODUCTION_AUDIT.md Section 3.
--
-- USAGE: Apply per tenant database. Test in DEV/UAT first. Review against
--        existing indexes (sp_helpindex 'tbl_xxx') — if a similar index already
--        exists, do NOT add a duplicate; consider modifying the existing one.
--
-- WARNING: Indexes have a write cost. On heavy-write tables (Invoice/Credit
--          Details), measure INSERT/UPDATE p99 before and after.
-- =============================================================================

USE [<TenantDatabaseName>];   -- <<< REPLACE before running
GO

SET NOCOUNT ON;
PRINT '--- BEFORE ---';
PRINT 'Existing indexes on tbl_InvoiceHeader:';
EXEC sp_helpindex 'tbl_InvoiceHeader';
PRINT 'Existing indexes on tbl_InvoiceDetails:';
EXEC sp_helpindex 'tbl_InvoiceDetails';
GO

-- ----------------------------------------------------------------------------
-- 1. tbl_InvoiceHeader — date range scan + cover commonly-read header columns
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvoiceHeader_DateTrans_Cover'
               AND object_id = OBJECT_ID('tbl_InvoiceHeader'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_InvoiceHeader_DateTrans_Cover
    ON tbl_InvoiceHeader (DateTrans)
    INCLUDE (pk_InvoiceID, fk_StoreCode, fk_CustomerCode, fk_AgentID,
             fk_PayTypeCode, fk_StationCode, fk_UserCode, fk_ZReport,
             SessionDateTime)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_InvoiceHeader_DateTrans_Cover';
END
GO

-- ----------------------------------------------------------------------------
-- 2. tbl_CreditHeader
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditHeader_DateTrans_Cover'
               AND object_id = OBJECT_ID('tbl_CreditHeader'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CreditHeader_DateTrans_Cover
    ON tbl_CreditHeader (DateTrans)
    INCLUDE (pk_CreditID, fk_StoreCode, fk_CustomerCode, fk_AgentID,
             fk_PayTypeCode, fk_StationCode, fk_UserCode, fk_ZReport,
             SessionDateTime)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_CreditHeader_DateTrans_Cover';
END
GO

-- ----------------------------------------------------------------------------
-- 3. tbl_PurchInvoiceHeader
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchInvoiceHeader_DateTrans_Cover'
               AND object_id = OBJECT_ID('tbl_PurchInvoiceHeader'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PurchInvoiceHeader_DateTrans_Cover
    ON tbl_PurchInvoiceHeader (DateTrans)
    INCLUDE (pk_PurchInvoiceID, fk_StoreCode, fk_SupplierCode,
             fk_StationCode, fk_UserCode, SessionDateTime)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_PurchInvoiceHeader_DateTrans_Cover';
END
GO

-- ----------------------------------------------------------------------------
-- 4. tbl_PurchReturnHeader
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchReturnHeader_DateTrans_Cover'
               AND object_id = OBJECT_ID('tbl_PurchReturnHeader'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PurchReturnHeader_DateTrans_Cover
    ON tbl_PurchReturnHeader (DateTrans)
    INCLUDE (pk_PurchReturnID, fk_StoreCode, fk_SupplierCode,
             fk_StationCode, fk_UserCode, SessionDateTime)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_PurchReturnHeader_DateTrans_Cover';
END
GO

-- ----------------------------------------------------------------------------
-- 5–8. Detail tables — fk_<Header> + cover columns we read
-- (Most tenants probably already have a clustered or NCI on fk_Header for FK
-- enforcement. Validate before applying — duplicate indexes hurt writes.)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvoiceDetails_FkInvoice_Cover'
               AND object_id = OBJECT_ID('tbl_InvoiceDetails'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_InvoiceDetails_FkInvoice_Cover
    ON tbl_InvoiceDetails (fk_Invoice)
    INCLUDE (fk_ItemID, Quantity, Amount, Discount, ExtraDiscount,
             VatAmount, ItemCost, fk_VatCode)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_InvoiceDetails_FkInvoice_Cover';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditDetails_FkCredit_Cover'
               AND object_id = OBJECT_ID('tbl_CreditDetails'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CreditDetails_FkCredit_Cover
    ON tbl_CreditDetails (fk_Credit)
    INCLUDE (fk_ItemID, Quantity, Amount, Discount, ExtraDiscount,
             VatAmount, ItemCost, fk_VatCode)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_CreditDetails_FkCredit_Cover';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchInvoiceDetails_FkHdr_Cover'
               AND object_id = OBJECT_ID('tbl_PurchInvoiceDetails'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PurchInvoiceDetails_FkHdr_Cover
    ON tbl_PurchInvoiceDetails (fk_PurchInvoiceID)
    INCLUDE (fk_ItemID, Quantity, Amount, Discount, ExtraDiscount,
             VatAmount, ItemCost, fk_VatCode)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_PurchInvoiceDetails_FkHdr_Cover';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchReturnDetails_FkHdr_Cover'
               AND object_id = OBJECT_ID('tbl_PurchReturnDetails'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PurchReturnDetails_FkHdr_Cover
    ON tbl_PurchReturnDetails (fk_PurchReturnID)
    INCLUDE (fk_ItemID, Quantity, Amount, Discount, ExtraDiscount,
             VatAmount, ItemCost, fk_VatCode)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_PurchReturnDetails_FkHdr_Cover';
END
GO

-- ----------------------------------------------------------------------------
-- 9. tbl_RelItemSuppliers — almost always filtered by PrimarySupplier=1
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RelItemSuppliers_Item_Primary'
               AND object_id = OBJECT_ID('tbl_RelItemSuppliers'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_RelItemSuppliers_Item_Primary
    ON tbl_RelItemSuppliers (fk_ItemID, PrimarySupplier)
    INCLUDE (fk_SupplierNo)
    WITH (FILLFACTOR = 90, ONLINE = OFF);
    PRINT 'Created IX_RelItemSuppliers_Item_Primary';
END
GO

-- ----------------------------------------------------------------------------
-- Update statistics on touched tables (one-shot after index creation)
-- ----------------------------------------------------------------------------
UPDATE STATISTICS tbl_InvoiceHeader   WITH FULLSCAN;
UPDATE STATISTICS tbl_CreditHeader    WITH FULLSCAN;
UPDATE STATISTICS tbl_PurchInvoiceHeader WITH FULLSCAN;
UPDATE STATISTICS tbl_PurchReturnHeader  WITH FULLSCAN;
UPDATE STATISTICS tbl_InvoiceDetails  WITH FULLSCAN;
UPDATE STATISTICS tbl_CreditDetails   WITH FULLSCAN;
UPDATE STATISTICS tbl_PurchInvoiceDetails WITH FULLSCAN;
UPDATE STATISTICS tbl_PurchReturnDetails  WITH FULLSCAN;
GO

PRINT '--- AFTER ---';
PRINT 'Done. Run a representative Catalogue query and capture STATISTICS TIME, IO.';
GO
