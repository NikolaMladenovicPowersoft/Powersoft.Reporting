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
            BreakdownType.Daily => "CONVERT(VARCHAR(10), DateTrans, 120)",  // YYYY-MM-DD
            BreakdownType.Weekly => "CONVERT(VARCHAR(4), DATEPART(YEAR, DateTrans)) + '-W' + RIGHT('00' + CONVERT(VARCHAR(2), DATEPART(WEEK, DateTrans)), 2)",
            BreakdownType.Monthly => "CONVERT(VARCHAR(7), DateTrans, 120)",  // YYYY-MM
            _ => "CONVERT(VARCHAR(7), DateTrans, 120)"
        };

        string groupByField = "";
        string groupByJoin = "";
        string groupBySelect = "";
        
        if (groupBy == GroupByType.Store)
        {
            groupBySelect = ", t1.fk_StoreCode AS GroupCode, ISNULL(s.StoreName, t1.fk_StoreCode) AS GroupName";
            groupByJoin = "LEFT JOIN tbl_Store s ON t1.fk_StoreCode = s.pk_StoreCode";
            groupByField = ", t1.fk_StoreCode, ISNULL(s.StoreName, t1.fk_StoreCode)";
        }

        string sql = $@"
            SELECT 
                {periodField} AS Period
                {groupBySelect},
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0) + ISNULL(t2.VatAmount, 0)) AS GrossSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            {groupByJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
            GROUP BY {periodField}{groupByField}
            
            UNION ALL
            
            SELECT 
                {periodField} AS Period
                {groupBySelect},
                0 AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0) + ISNULL(t2.VatAmount, 0)) AS GrossSales
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            {groupByJoin.Replace("t1.fk_StoreCode", "t1.fk_StoreCode")}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo
            GROUP BY {periodField}{groupByField}
            
            ORDER BY Period";

        // Simplified query for now (no grouping complexity)
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
                CYInvoiceCount = reader.GetInt32(1),
                CYCreditCount = reader.GetInt32(2),
                CYQtySold = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                CYQtyReturned = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                CYNetSales = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                CYNetReturns = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                CYVatSales = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                CYVatReturns = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8)
            };
            
            row.CYGrossSales = row.CYNetSales + row.CYVatSales;
            row.CYGrossReturns = row.CYNetReturns + row.CYVatReturns;
            
            results.Add(row);
        }
        
        return results;
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
