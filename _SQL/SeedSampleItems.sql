-- Seed Sample Items for Powersoft.Reporting (Average Basket Item filter testing)
-- Run against: TENANT database
-- Prerequisites: tbl_Item exists. tbl_Vat (or equivalent) should have at least one VAT code.
-- If your DB uses different VAT codes, change @VatCode below.

SET NOCOUNT ON;

DECLARE @VatCode NVARCHAR(3) = '0';  -- Common zero VAT. Change if your DB uses different codes (e.g. 'A1', 'B1').

-- Ensure we have a usable VAT code: try common codes if tbl_Vat exists
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tbl_Vat')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tbl_Vat WHERE pk_VatCode = @VatCode)
    BEGIN
        SELECT TOP 1 @VatCode = pk_VatCode FROM tbl_Vat;
        IF @VatCode IS NULL SET @VatCode = '0';
    END
END

-- Insert sample items (skip if ItemCode already exists)
IF NOT EXISTS (SELECT 1 FROM tbl_Item WHERE ItemCode = 'DEMO001')
BEGIN
    INSERT INTO tbl_Item (ItemCode, ItemNamePrimary, ItemNameSecondary, ItemActive, fk_VatCode)
    VALUES
        ('DEMO001', N'Sample Product A - Coffee', N'Premium blend', 1, @VatCode),
        ('DEMO002', N'Sample Product B - Tea', N'Green tea', 1, @VatCode),
        ('DEMO003', N'Sample Product C - Biscuits', N'Chocolate', 1, @VatCode),
        ('DEMO004', N'Sample Product D - Water', NULL, 1, @VatCode),
        ('DEMO005', N'Sample Product E - Juice', N'Orange', 1, @VatCode),
        ('DEMO006', N'Sample Product F - Snacks', N'Mixed nuts', 1, @VatCode),
        ('DEMO007', N'Sample Product G - Milk', NULL, 1, @VatCode),
        ('DEMO008', N'Sample Product H - Bread', N'Whole grain', 1, @VatCode),
        ('DEMO009', N'Sample Product I - Cheese', N'Cheddar', 1, @VatCode),
        ('DEMO010', N'Sample Product J - Yogurt', N'Natural', 1, @VatCode);

    PRINT 'Inserted 10 sample items (DEMO001-DEMO010).';
END
ELSE
BEGIN
    PRINT 'Sample items already exist. Skipping insert.';
END

-- Optional: If you need sample INVOICE/CREDIT data to test the report with item filter,
-- you would need: tbl_Store, tbl_InvoiceHeader, tbl_InvoiceDetails, tbl_CreditHeader, tbl_CreditDetails.
-- Those depend on your tenant schema (Station, Customer, User, etc.). 
-- Run your main app's demo/seed if available, or create invoices via the POS/ERP UI.

GO
