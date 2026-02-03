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
        GroupByType groupBy = GroupByType.None)
    {
        var results = new List<AverageBasketRow>();
        
        string periodField = breakdown switch
        {
            BreakdownType.Daily => "CONVERT(VARCHAR(10), t1.DateTrans, 120)",
            BreakdownType.Weekly => "CONVERT(VARCHAR(4), DATEPART(YEAR, t1.DateTrans)) + '-W' + RIGHT('00' + CONVERT(VARCHAR(2), DATEPART(WEEK, t1.DateTrans)), 2)",
            BreakdownType.Monthly => "CONVERT(VARCHAR(7), t1.DateTrans, 120)",
            _ => "CONVERT(VARCHAR(7), t1.DateTrans, 120)"
        };

        // Build grouping components based on GroupByType
        var (groupSelect, groupJoin, groupField, groupOrderBy) = GetGroupingComponents(groupBy);
        
        string sql;
        
        if (groupBy == GroupByType.None)
        {
            // Simple query without grouping dimension
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
                )
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
                ORDER BY COALESCE(s.Period, r.Period)";
        }
        else
        {
            // Query with grouping dimension (Store, Category, etc.)
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
                )
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
                ORDER BY {groupOrderBy}, COALESCE(s.Period, r.Period)";
        }

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", dateTo.Date);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
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
