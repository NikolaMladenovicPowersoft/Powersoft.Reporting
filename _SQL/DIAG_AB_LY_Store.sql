-- DIAGNOSTIC: Average Basket - Yesterday, By Store, Compare Last Year
-- Run against the SAME tenant DB that George is using (e.g. pswaSplash)

DECLARE @DateFrom DATE = '2026-06-16';
DECLARE @DateTo DATE = '2026-06-16';

-- ============================================================
-- STEP 1: Run ONLY this block first to check if data exists.
-- If CY_Sales = 0 -> wrong DB or wrong date.
-- Use Latest_Invoice_Date to find the right @DateFrom to test.
-- ============================================================
SELECT 'CY_Sales' AS Source,
       COUNT(*) AS RecordCount,
       MAX(CONVERT(DATE, DateTrans)) AS MaxDate
FROM tbl_InvoiceHeader
WHERE CONVERT(DATE, DateTrans) = @DateFrom
UNION ALL
SELECT 'LY_Sales',
       COUNT(*),
       MAX(CONVERT(DATE, DateTrans))
FROM tbl_InvoiceHeader
WHERE CONVERT(DATE, DateTrans) = DATEADD(YEAR, -1, @DateFrom)
UNION ALL
SELECT 'Latest_Invoice_Date',
       COUNT(*),
       MAX(CONVERT(DATE, DateTrans))
FROM tbl_InvoiceHeader;

-- ============================================================
-- STEP 2: If CY_Sales > 0 above, update @DateFrom and run this.
-- This is what the repository generates for:
--   GroupBy=Store, Breakdown=Daily, CompareLastYear=true
-- ============================================================
;WITH Sales AS (
    SELECT 
        CONVERT(VARCHAR(10), t1.DateTrans, 120) AS Period,
        t1.fk_StoreCode AS GroupCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END AS GroupName,
        ISNULL(st1.StoreArea, 0) AS StoreArea,
        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
        SUM(t2.Quantity) AS QtySold,
        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
    FROM tbl_InvoiceHeader t1
    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
    LEFT JOIN tbl_Store st1 ON t1.fk_StoreCode = st1.pk_StoreCode
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
    GROUP BY CONVERT(VARCHAR(10), t1.DateTrans, 120), t1.fk_StoreCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END,
        ISNULL(st1.StoreArea, 0)
),
Returns AS (
    SELECT 
        CONVERT(VARCHAR(10), t1.DateTrans, 120) AS Period,
        t1.fk_StoreCode AS GroupCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END AS GroupName,
        ISNULL(st1.StoreArea, 0) AS StoreArea,
        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
        SUM(t2.Quantity) AS QtyReturned,
        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
    FROM tbl_CreditHeader t1
    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
    LEFT JOIN tbl_Store st1 ON t1.fk_StoreCode = st1.pk_StoreCode
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
    GROUP BY CONVERT(VARCHAR(10), t1.DateTrans, 120), t1.fk_StoreCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END,
        ISNULL(st1.StoreArea, 0)
),
CY AS (
    SELECT 
        COALESCE(s.Period, r.Period) AS Period,
        COALESCE(s.GroupCode, r.GroupCode) AS GroupCode,
        COALESCE(s.GroupName, r.GroupName) AS GroupName,
        NULL AS Group2Code, NULL AS Group2Name,
        COALESCE(s.StoreArea, r.StoreArea) AS StoreArea,
        ISNULL(s.InvoiceCount, 0) AS InvoiceCount,
        ISNULL(r.CreditCount, 0) AS CreditCount,
        ISNULL(s.QtySold, 0) AS QtySold,
        ISNULL(r.QtyReturned, 0) AS QtyReturned,
        ISNULL(s.NetSales, 0) AS NetSales,
        ISNULL(r.NetReturns, 0) AS NetReturns,
        ISNULL(s.VatSales, 0) AS VatSales,
        ISNULL(r.VatReturns, 0) AS VatReturns
    FROM Sales s
    FULL OUTER JOIN Returns r ON s.Period = r.Period AND s.GroupCode = r.GroupCode
),
LYSales AS (
    SELECT 
        CONVERT(VARCHAR(10), DATEADD(YEAR, 1, t1.DateTrans), 120) AS Period,
        t1.fk_StoreCode AS GroupCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END AS GroupName,
        ISNULL(st1.StoreArea, 0) AS StoreArea,
        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
        SUM(t2.Quantity) AS QtySold,
        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
    FROM tbl_InvoiceHeader t1
    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
    LEFT JOIN tbl_Store st1 ON t1.fk_StoreCode = st1.pk_StoreCode
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
    GROUP BY CONVERT(VARCHAR(10), DATEADD(YEAR, 1, t1.DateTrans), 120), t1.fk_StoreCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END,
        ISNULL(st1.StoreArea, 0)
),
LYReturns AS (
    SELECT 
        CONVERT(VARCHAR(10), DATEADD(YEAR, 1, t1.DateTrans), 120) AS Period,
        t1.fk_StoreCode AS GroupCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END AS GroupName,
        ISNULL(st1.StoreArea, 0) AS StoreArea,
        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
        SUM(t2.Quantity) AS QtyReturned,
        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
    FROM tbl_CreditHeader t1
    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
    LEFT JOIN tbl_Store st1 ON t1.fk_StoreCode = st1.pk_StoreCode
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
    GROUP BY CONVERT(VARCHAR(10), DATEADD(YEAR, 1, t1.DateTrans), 120), t1.fk_StoreCode,
        CASE WHEN ISNULL(st1.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' 
             ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st1.StoreName, t1.fk_StoreCode))) END,
        ISNULL(st1.StoreArea, 0)
),
LY AS (
    SELECT 
        COALESCE(lys.Period, lyr.Period) AS Period,
        COALESCE(lys.GroupCode, lyr.GroupCode) AS GroupCode,
        COALESCE(lys.GroupName, lyr.GroupName) AS GroupName,
        COALESCE(lys.StoreArea, lyr.StoreArea) AS StoreArea,
        ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
        ISNULL(lyr.CreditCount, 0) AS CreditCount,
        ISNULL(lys.QtySold, 0) AS QtySold,
        ISNULL(lyr.QtyReturned, 0) AS QtyReturned,
        ISNULL(lys.NetSales, 0) AS NetSales,
        ISNULL(lyr.NetReturns, 0) AS NetReturns,
        ISNULL(lys.VatSales, 0) AS VatSales,
        ISNULL(lyr.VatReturns, 0) AS VatReturns
    FROM LYSales lys
    FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period AND lys.GroupCode = lyr.GroupCode
)
-- FINAL: Compare CY vs LY side by side
SELECT 
    cy.GroupCode AS Store,
    cy.GroupName AS StoreName,
    cy.InvoiceCount AS CY_Invoices,
    cy.NetSales AS CY_NetSales,
    ISNULL(ly.InvoiceCount, 0) AS LY_Invoices,
    ISNULL(ly.NetSales, 0) AS LY_NetSales,
    CASE WHEN cy.NetSales = ISNULL(ly.NetSales, 0) THEN '*** SAME ***' ELSE 'OK (different)' END AS AmountCheck
FROM CY cy
LEFT JOIN LY ly ON ly.Period = cy.Period AND ly.GroupCode = cy.GroupCode
ORDER BY cy.GroupCode;
