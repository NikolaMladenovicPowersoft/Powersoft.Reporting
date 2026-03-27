namespace Powersoft.Reporting.Web.Services.AI;

/// <summary>
/// Provides a compact, curated SQL Server schema description for the AI text-to-SQL pipeline.
/// Only the most important business tables and columns are included to keep token usage low.
/// </summary>
public static class SchemaProvider
{
    /// <summary>
    /// Returns a compact DDL-like schema description (~2-3K tokens) covering the core
    /// business tables a retail/accounting tenant database contains.
    /// </summary>
    public static string GetCompactSchema()
    {
        return @"
-- SALES (invoices / receipts)
tbl_InvoiceHeader (
  pk_InvoiceID NVARCHAR PK,          -- invoice ID
  fk_StoreCode NVARCHAR,             -- FK → tbl_Store
  fk_CustomerCode NVARCHAR,          -- FK → tbl_Customer.pk_CustomerNo
  fk_AgentID BIGINT,                 -- FK → tbl_Agent
  InvoiceTotal DECIMAL,              -- subtotal before VAT
  InvoiceVat DECIMAL,                -- VAT amount
  InvoiceDiscount DECIMAL,           -- discount amount
  InvoiceGrandTotal DECIMAL,         -- final total incl VAT
  DateTrans DATETIME,                -- transaction date
  fk_TransCode NVARCHAR,             -- document type code
  Comments NVARCHAR,
  fk_UserCode NVARCHAR               -- cashier/user
)
tbl_InvoiceDetails (
  pk_InvoiceDetailsID BIGINT PK IDENTITY,
  fk_Invoice NVARCHAR,               -- FK → tbl_InvoiceHeader.pk_InvoiceID
  fk_ItemID BIGINT,                  -- FK → tbl_Item.pk_ItemID
  fk_StoreCode NVARCHAR,             -- FK → tbl_Store
  Quantity DECIMAL,
  Amount DECIMAL,                    -- line total before discount
  Discount DECIMAL,
  ExtraDiscount DECIMAL,
  VatAmount DECIMAL,
  CostPrice DECIMAL
)

-- SALES RETURNS (credits)
tbl_CreditHeader (
  pk_CreditID NVARCHAR PK,
  fk_StoreCode NVARCHAR,
  fk_CustomerCode NVARCHAR,
  CreditTotal DECIMAL,
  CreditVat DECIMAL,
  CreditDiscount DECIMAL,
  CreditGrandTotal DECIMAL,
  DateTrans DATETIME,
  fk_TransCode NVARCHAR
)
tbl_CreditDetails (
  pk_CreditDetailsID BIGINT PK IDENTITY,
  fk_Credit NVARCHAR,                -- FK → tbl_CreditHeader.pk_CreditID
  fk_ItemID BIGINT,
  fk_StoreCode NVARCHAR,
  Quantity DECIMAL,
  Amount DECIMAL,
  Discount DECIMAL,
  ExtraDiscount DECIMAL,
  VatAmount DECIMAL
)

-- PURCHASES
tbl_PurchInvoiceHeader (
  pk_PurchInvoiceID NVARCHAR PK,
  fk_StoreCode NVARCHAR,
  fk_SupplierNo NVARCHAR,            -- FK → tbl_Supplier.pk_SupplierNo
  PurchInvoiceTotal DECIMAL,
  PurchInvoiceVat DECIMAL,
  PurchInvoiceGrandTotal DECIMAL,
  DateTrans DATETIME,
  fk_TransCode NVARCHAR
)
tbl_PurchInvoiceDetails (
  pk_PurchInvoiceDetailsID BIGINT PK IDENTITY,
  fk_PurchInvoiceID NVARCHAR,
  fk_ItemID BIGINT,
  fk_StoreCode NVARCHAR,
  Quantity DECIMAL,
  Amount DECIMAL,
  Discount DECIMAL,
  ExtraDiscount DECIMAL,
  VatAmount DECIMAL,
  CostPrice DECIMAL
)

-- PURCHASE RETURNS
tbl_PurchReturnHeader (
  pk_PurchReturnID NVARCHAR PK,
  fk_StoreCode NVARCHAR,
  fk_SupplierNo NVARCHAR,
  PurchReturnTotal DECIMAL,
  PurchReturnVat DECIMAL,
  PurchReturnGrandTotal DECIMAL,
  DateTrans DATETIME
)
tbl_PurchReturnDetails (
  pk_PurchReturnDetailsID BIGINT PK IDENTITY,
  fk_PurchReturnID NVARCHAR,
  fk_ItemID BIGINT,
  fk_StoreCode NVARCHAR,
  Quantity DECIMAL,
  Amount DECIMAL,
  Discount DECIMAL,
  ExtraDiscount DECIMAL,
  VatAmount DECIMAL
)

-- ITEMS (products)
tbl_Item (
  pk_ItemID BIGINT PK IDENTITY,
  ItemCode NVARCHAR(50) UNIQUE,
  ItemNamePrimary NVARCHAR(100),
  ItemNameSecondary NVARCHAR(100),
  ItemActive BIT DEFAULT 1,
  fk_CategoryID BIGINT,              -- FK → tbl_ItemCategory
  fk_DepartmentID BIGINT,            -- FK → tbl_ItemDepartment
  fk_BrandID BIGINT,                 -- FK → tbl_Brands
  fk_SeasonID BIGINT,                -- FK → tbl_Season
  fk_ModelID BIGINT,                 -- FK → tbl_Model
  fk_ColourID BIGINT,                -- FK → tbl_Colour
  fk_SizeID BIGINT,                  -- FK → tbl_Size
  fk_VatID BIGINT,                   -- FK → tbl_Vat
  RetailPrice DECIMAL,
  WholesalePrice DECIMAL,
  CostPrice DECIMAL,
  TotalStockQty DECIMAL,             -- current stock quantity
  ECommerce BIT,                     -- is e-shop item
  Storable BIT,
  LastModifiedDate DATETIME,
  CreationDate DATETIME,
  ReleaseDate DATETIME,
  Barcode NVARCHAR
)

-- ITEM LOOKUPS
tbl_ItemCategory (pk_CategoryID BIGINT PK, CategoryCode NVARCHAR, CategoryDescr NVARCHAR)
tbl_ItemDepartment (pk_DepartmentID BIGINT PK, DepartmentCode NVARCHAR, DepartmentDescr NVARCHAR)
tbl_Brands (pk_BrandID BIGINT PK, BrandCode NVARCHAR, BrandDesc NVARCHAR)
tbl_Season (pk_SeasonID BIGINT PK, SeasonCode NVARCHAR, SeasonDescr NVARCHAR)
tbl_Model (pk_ModelID BIGINT PK, ModelCode NVARCHAR, ModelDesc NVARCHAR)

-- CUSTOMERS
tbl_Customer (
  pk_CustomerNo NVARCHAR PK,         -- customer code
  Company BIT,                       -- 1=company, 0=individual
  LastCompanyName NVARCHAR,           -- surname or company name
  FirstName NVARCHAR,
  Email NVARCHAR,
  Phone NVARCHAR,
  fk_CustCategory NVARCHAR,          -- FK → tbl_CustCategory
  CustomerActive BIT,
  CreationDate DATETIME,
  fk_Country NVARCHAR
)
tbl_CustCategory (pk_CustCatCode NVARCHAR PK, CustCatDescr NVARCHAR)

-- SUPPLIERS
tbl_Supplier (
  pk_SupplierNo NVARCHAR PK,
  Company BIT,
  LastCompanyName NVARCHAR,
  FirstName NVARCHAR,
  Email NVARCHAR,
  Phone NVARCHAR,
  SupplierActive BIT
)

-- STORES
tbl_Store (
  pk_StoreCode NVARCHAR PK,
  StoreName NVARCHAR,
  StoreActive BIT,
  Address NVARCHAR,
  City NVARCHAR,
  Phone NVARCHAR
)

-- Z-REPORTS (daily register closings)
tbl_ZReport (
  pk_ZReportID NVARCHAR PK,
  fk_StoreCode NVARCHAR,
  DateZ DATETIME,                    -- Z-report date
  GrandTotal DECIMAL,
  VatTotal DECIMAL,
  DiscountTotal DECIMAL,
  fk_UserCode NVARCHAR
)
tbl_ZReportDetail (
  pk_ZReportDetailID BIGINT PK IDENTITY,
  fk_ZReport NVARCHAR,
  fk_PayTypeCode NVARCHAR,
  PayAmount DECIMAL,
  PayDescription NVARCHAR
)

-- ITEM-SUPPLIER LINK
tbl_RelItemSuppliers (
  fk_ItemID BIGINT,                  -- FK → tbl_Item.pk_ItemID
  fk_SupplierNo NVARCHAR,            -- FK → tbl_Supplier.pk_SupplierNo
  PrimarySupplier BIT,
  SupplierCost DECIMAL
)

-- STOCK PER STORE
tbl_RelItemStore (
  fk_ItemID BIGINT,
  fk_StoreCode NVARCHAR,
  StockQty DECIMAL                   -- stock at specific store
)

-- IMPORTANT RELATIONSHIPS:
-- tbl_InvoiceDetails.fk_Invoice → tbl_InvoiceHeader.pk_InvoiceID
-- tbl_InvoiceDetails.fk_ItemID → tbl_Item.pk_ItemID
-- tbl_InvoiceHeader.fk_CustomerCode → tbl_Customer.pk_CustomerNo
-- tbl_InvoiceHeader.fk_StoreCode → tbl_Store.pk_StoreCode
-- tbl_Item.fk_CategoryID → tbl_ItemCategory.pk_CategoryID
-- tbl_Item.fk_DepartmentID → tbl_ItemDepartment.pk_DepartmentID
-- tbl_Item.fk_BrandID → tbl_Brands.pk_BrandID
-- tbl_Item.fk_SeasonID → tbl_Season.pk_SeasonID
-- Net sales amount = Amount - (Discount + ExtraDiscount)
-- Gross sales amount = Amount - (Discount + ExtraDiscount) + VatAmount
".Trim();
    }
}
