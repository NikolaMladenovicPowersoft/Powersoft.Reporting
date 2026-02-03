using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class AverageBasketRepository
{
    private readonly string _connectionString;

    public AverageBasketRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<AverageBasketRow>> GetAverageBasketDataAsync(
        DateTime dateFrom,
        DateTime dateTo,
        BreakdownType breakdown = BreakdownType.Monthly,
        GroupByType groupBy = GroupByType.None,
        bool includeLastYear = false)
    {
        var results = new List<AverageBasketRow>();
        
        string periodField = breakdown switch
        {
            BreakdownType.Daily => "CONVERT(VARCHAR(10), t1.DateTrans, 120)",
            BreakdownType.Weekly => "CONVERT(VARCHAR(4), DATEPART(YEAR, t1.DateTrans)) + '-W' + RIGHT('00' + CONVERT(VARCHAR(2), DATEPART(WEEK, t1.DateTrans)), 2)",
            BreakdownType.Monthly => "CONVERT(VARCHAR(7), t1.DateTrans, 120)",
            _ => "CONVERT(VARCHAR(7), t1.DateTrans, 120)"
        };

        var (groupSelect, groupJoin, groupField, groupOrderBy) = GetGroupingComponents(groupBy);
        
        string sql;
        string lyJoinCondition = "ly.Period = CAST(CAST(SUBSTRING(cy.Period, 1, 4) AS INT) - 1 AS VARCHAR(4)) + SUBSTRING(cy.Period, 5, 20)";
        string lyGroupMatch = groupBy != GroupByType.None ? " AND ly.GroupCode = cy.GroupCode" : "";
        
        if (groupBy == GroupByType.None)
        {
            var lyFrom = includeLastYear ? $@"
                , LYSales AS (
                    SELECT 
                        {periodField} AS Period,
                        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
                    FROM tbl_InvoiceHeader t1
                    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
                    GROUP BY {periodField}
                ),
                LYReturns AS (
                    SELECT 
                        {periodField} AS Period,
                        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                    FROM tbl_CreditHeader t1
                    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
                    GROUP BY {periodField}
                ),
                LY AS (
                    SELECT 
                        COALESCE(lys.Period, lyr.Period) AS Period,
                        ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                        ISNULL(lyr.CreditCount, 0) AS CreditCount,
                        ISNULL(lys.NetSales, 0) AS NetSales,
                        ISNULL(lyr.NetReturns, 0) AS NetReturns,
                        ISNULL(lys.VatSales, 0) AS VatSales,
                        ISNULL(lyr.VatReturns, 0) AS VatReturns
                    FROM LYSales lys
                    FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period
                )" : "";
            
            var lyJoin = includeLastYear ? $@"
                LEFT JOIN LY ly ON {lyJoinCondition}" : "";
            
            sql = $@"
                ;WITH Sales AS (
                    SELECT 
                        {periodField} AS Period,
                        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                        SUM(t2.Quantity) AS QtySold,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
                    FROM tbl_InvoiceHeader t1
                    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
                    GROUP BY {periodField}
                ),
                Returns AS (
                    SELECT 
                        {periodField} AS Period,
                        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                        SUM(t2.Quantity) AS QtyReturned,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                    FROM tbl_CreditHeader t1
                    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
                    GROUP BY {periodField}
                ),
                CY AS (
                    SELECT 
                        COALESCE(s.Period, r.Period) AS Period,
                        NULL AS GroupCode,
                        NULL AS GroupName,
                        ISNULL(s.InvoiceCount, 0) AS InvoiceCount,
                        ISNULL(r.CreditCount, 0) AS CreditCount,
                        ISNULL(s.QtySold, 0) AS QtySold,
                        ISNULL(r.QtyReturned, 0) AS QtyReturned,
                        ISNULL(s.NetSales, 0) AS NetSales,
                        ISNULL(r.NetReturns, 0) AS NetReturns,
                        ISNULL(s.VatSales, 0) AS VatSales,
                        ISNULL(r.VatReturns, 0) AS VatReturns
                    FROM Sales s
                    FULL OUTER JOIN Returns r ON s.Period = r.Period
                ){lyFrom}
                SELECT 
                    cy.Period,
                    cy.GroupCode,
                    cy.GroupName,
                    cy.InvoiceCount,
                    cy.CreditCount,
                    cy.QtySold,
                    cy.QtyReturned,
                    cy.NetSales,
                    cy.NetReturns,
                    cy.VatSales,
                    cy.VatReturns{(includeLastYear ? @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.NetSales, 0) AS LYNetSales,
                    ISNULL(ly.NetReturns, 0) AS LYNetReturns,
                    ISNULL(ly.VatSales, 0) AS LYVatSales,
                    ISNULL(ly.VatReturns, 0) AS LYVatReturns" : "")}
                FROM CY cy
                {lyJoin}
                ORDER BY cy.Period";
        }
        else
        {
            var lyFrom = includeLastYear ? $@"
                , LYSales AS (
                    SELECT 
                        {periodField} AS Period,
                        {groupSelect}
                        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
                    FROM tbl_InvoiceHeader t1
                    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                    {groupJoin}
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
                    GROUP BY {periodField}, {groupField}
                ),
                LYReturns AS (
                    SELECT 
                        {periodField} AS Period,
                        {groupSelect}
                        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                    FROM tbl_CreditHeader t1
                    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                    {groupJoin.Replace("t2.fk_Invoice", "t2.fk_Credit")}
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo)
                    GROUP BY {periodField}, {groupField}
                ),
                LY AS (
                    SELECT 
                        COALESCE(lys.Period, lyr.Period) AS Period,
                        COALESCE(lys.GroupCode, lyr.GroupCode) AS GroupCode,
                        COALESCE(lys.GroupName, lyr.GroupName) AS GroupName,
                        ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                        ISNULL(lyr.CreditCount, 0) AS CreditCount,
                        ISNULL(lys.NetSales, 0) AS NetSales,
                        ISNULL(lyr.NetReturns, 0) AS NetReturns,
                        ISNULL(lys.VatSales, 0) AS VatSales,
                        ISNULL(lyr.VatReturns, 0) AS VatReturns
                    FROM LYSales lys
                    FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period AND lys.GroupCode = lyr.GroupCode
                )" : "";
            
            var lyJoin = includeLastYear ? $@"
                LEFT JOIN LY ly ON ly.Period = CAST(CAST(SUBSTRING(cy.Period, 1, 4) AS INT) - 1 AS VARCHAR(4)) + SUBSTRING(cy.Period, 5, 20){lyGroupMatch}" : "";
            
            sql = $@"
                ;WITH Sales AS (
                    SELECT 
                        {periodField} AS Period,
                        {groupSelect}
                        COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                        SUM(t2.Quantity) AS QtySold,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
                    FROM tbl_InvoiceHeader t1
                    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                    {groupJoin}
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
                    GROUP BY {periodField}, {groupField}
                ),
                Returns AS (
                    SELECT 
                        {periodField} AS Period,
                        {groupSelect}
                        COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                        SUM(t2.Quantity) AS QtyReturned,
                        SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                        SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                    FROM tbl_CreditHeader t1
                    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                    {groupJoin.Replace("t2.fk_Invoice", "t2.fk_Credit")}
                    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
                    GROUP BY {periodField}, {groupField}
                ),
                CY AS (
                    SELECT 
                        COALESCE(s.Period, r.Period) AS Period,
                        COALESCE(s.GroupCode, r.GroupCode) AS GroupCode,
                        COALESCE(s.GroupName, r.GroupName) AS GroupName,
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
                ){lyFrom}
                SELECT 
                    cy.Period,
                    cy.GroupCode,
                    cy.GroupName,
                    cy.InvoiceCount,
                    cy.CreditCount,
                    cy.QtySold,
                    cy.QtyReturned,
                    cy.NetSales,
                    cy.NetReturns,
                    cy.VatSales,
                    cy.VatReturns{(includeLastYear ? @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.NetSales, 0) AS LYNetSales,
                    ISNULL(ly.NetReturns, 0) AS LYNetReturns,
                    ISNULL(ly.VatSales, 0) AS LYVatSales,
                    ISNULL(ly.VatReturns, 0) AS LYVatReturns" : "")}
                FROM CY cy
                {lyJoin}
                ORDER BY {groupOrderBy}, cy.Period";
        }

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", dateTo.Date);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        var hasLy = includeLastYear;
        
        while (await reader.ReadAsync())
        {
            var row = new AverageBasketRow
            {
                Period = reader.GetString(0),
                Level1 = reader.IsDBNull(1) ? null : reader.GetString(1),
                Level1Value = reader.IsDBNull(2) ? null : reader.GetString(2),
                CYInvoiceCount = reader.GetInt32(3),
                CYCreditCount = reader.GetInt32(4),
                CYQtySold = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                CYQtyReturned = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                CYNetSales = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                CYNetReturns = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                CYVatSales = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                CYVatReturns = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10)
            };
            
            row.CYGrossSales = row.CYNetSales + row.CYVatSales;
            row.CYGrossReturns = row.CYNetReturns + row.CYVatReturns;
            
            if (hasLy)
            {
                row.LYInvoiceCount = reader.GetInt32(11);
                row.LYCreditCount = reader.GetInt32(12);
                row.LYNetSales = reader.IsDBNull(13) ? 0 : reader.GetDecimal(13);
                row.LYNetReturns = reader.IsDBNull(14) ? 0 : reader.GetDecimal(14);
                row.LYVatSales = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15);
                row.LYVatReturns = reader.IsDBNull(16) ? 0 : reader.GetDecimal(16);
                row.LYTotalNet = row.LYNetSales - row.LYNetReturns;
                row.LYTotalGross = row.LYTotalNet + (row.LYVatSales - row.LYVatReturns);
            }
            
            results.Add(row);
        }
        
        return results;
    }
    
    private (string select, string join, string field, string orderBy) GetGroupingComponents(GroupByType groupBy)
    {
        return groupBy switch
        {
            GroupByType.Store => (
                select: "t1.fk_StoreCode AS GroupCode, ISNULL(st.StoreName, t1.fk_StoreCode) AS GroupName,",
                join: "LEFT JOIN tbl_Store st ON t1.fk_StoreCode = st.pk_StoreCode",
                field: "t1.fk_StoreCode, ISNULL(st.StoreName, t1.fk_StoreCode)",
                orderBy: "COALESCE(s.GroupName, r.GroupName)"
            ),
            GroupByType.Category => (
                select: "CAST(ISNULL(t3.fk_CategoryID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(cat.CategoryDescr, 'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_ItemCategory cat ON t3.fk_CategoryID = cat.pk_CategoryID",
                field: "CAST(ISNULL(t3.fk_CategoryID, 0) AS NVARCHAR(20)), ISNULL(cat.CategoryDescr, 'N/A')",
                orderBy: "COALESCE(s.GroupName, r.GroupName)"
            ),
            GroupByType.Department => (
                select: "CAST(ISNULL(t3.fk_DepartmentID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(dep.DeptDescr, 'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_ItemDept dep ON t3.fk_DepartmentID = dep.pk_DeptID",
                field: "CAST(ISNULL(t3.fk_DepartmentID, 0) AS NVARCHAR(20)), ISNULL(dep.DeptDescr, 'N/A')",
                orderBy: "COALESCE(s.GroupName, r.GroupName)"
            ),
            GroupByType.Brand => (
                select: "CAST(ISNULL(t3.fk_BrandID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(br.BrandDescr, 'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_ItemBrand br ON t3.fk_BrandID = br.pk_BrandID",
                field: "CAST(ISNULL(t3.fk_BrandID, 0) AS NVARCHAR(20)), ISNULL(br.BrandDescr, 'N/A')",
                orderBy: "COALESCE(s.GroupName, r.GroupName)"
            ),
            _ => ("", "", "", "")
        };
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            
            // Quick test query
            using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            
            return true;
        }
        catch
        {
            return false;
        }
    }
}
