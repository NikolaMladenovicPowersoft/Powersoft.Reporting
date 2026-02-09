using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class AverageBasketRepository : IAverageBasketRepository
{
    private readonly string _connectionString;

    public AverageBasketRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PagedResult<AverageBasketRow>> GetAverageBasketDataAsync(ReportFilter filter)
    {
        var periodField = GetPeriodField(filter.Breakdown);
        var grouping = GetGroupingComponents(filter.GroupBy);
        var storeFilter = BuildStoreFilter(filter.StoreCodes);
        
        var sql = BuildMainQuery(periodField, grouping, storeFilter, filter);
        var countSql = BuildCountQuery(periodField, grouping, storeFilter, filter);
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        int totalCount = await GetTotalCountAsync(conn, countSql, filter);
        var results = await ExecuteMainQueryAsync(conn, sql, filter, grouping.hasGrouping);
        
        return new PagedResult<AverageBasketRow>
        {
            Items = results,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<List<AverageBasketRow>> GetAverageBasketDataAsync(
        DateTime dateFrom,
        DateTime dateTo,
        BreakdownType breakdown = BreakdownType.Monthly,
        GroupByType groupBy = GroupByType.None,
        bool includeLastYear = false)
    {
        var filter = new ReportFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Breakdown = breakdown,
            GroupBy = groupBy,
            CompareLastYear = includeLastYear,
            PageSize = int.MaxValue // No pagination for legacy calls
        };
        
        var result = await GetAverageBasketDataAsync(filter);
        return result.Items;
    }

    #region SQL Building

    private string GetPeriodField(BreakdownType breakdown)
    {
        return breakdown switch
        {
            BreakdownType.Daily => "CONVERT(VARCHAR(10), t1.DateTrans, 120)",
            BreakdownType.Weekly => "CONVERT(VARCHAR(4), DATEPART(YEAR, t1.DateTrans)) + '-W' + RIGHT('00' + CONVERT(VARCHAR(2), DATEPART(WEEK, t1.DateTrans)), 2)",
            BreakdownType.Monthly => "CONVERT(VARCHAR(7), t1.DateTrans, 120)",
            _ => "CONVERT(VARCHAR(7), t1.DateTrans, 120)"
        };
    }

    private (string select, string join, string field, string groupCodeOnly, string orderBy, bool hasGrouping) GetGroupingComponents(GroupByType groupBy)
    {
        return groupBy switch
        {
            GroupByType.Store => (
                select: "t1.fk_StoreCode AS GroupCode, ISNULL(st.StoreName, t1.fk_StoreCode) AS GroupName,",
                join: "LEFT JOIN tbl_Store st ON t1.fk_StoreCode = st.pk_StoreCode",
                field: "t1.fk_StoreCode, ISNULL(st.StoreName, t1.fk_StoreCode)",
                groupCodeOnly: "t1.fk_StoreCode",
                orderBy: "cy.GroupName",
                hasGrouping: true
            ),
            GroupByType.Category => (
                select: "CAST(ISNULL(t3.fk_CategoryID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(cat.CategoryDescr, 'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_ItemCategory cat ON t3.fk_CategoryID = cat.pk_CategoryID",
                field: "CAST(ISNULL(t3.fk_CategoryID, 0) AS NVARCHAR(20)), ISNULL(cat.CategoryDescr, 'N/A')",
                groupCodeOnly: "CAST(ISNULL(t3.fk_CategoryID, 0) AS NVARCHAR(20))",
                orderBy: "cy.GroupName",
                hasGrouping: true
            ),
            GroupByType.Department => (
                select: "CAST(ISNULL(t3.fk_DepartmentID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(dep.DepartmentDescr, N'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_ItemDepartment dep ON t3.fk_DepartmentID = dep.pk_DepartmentID",
                field: "CAST(ISNULL(t3.fk_DepartmentID, 0) AS NVARCHAR(20)), ISNULL(dep.DepartmentDescr, N'N/A')",
                groupCodeOnly: "CAST(ISNULL(t3.fk_DepartmentID, 0) AS NVARCHAR(20))",
                orderBy: "cy.GroupName",
                hasGrouping: true
            ),
            GroupByType.Brand => (
                select: "CAST(ISNULL(t3.fk_BrandID, 0) AS NVARCHAR(20)) AS GroupCode, ISNULL(br.BrandDesc, N'N/A') AS GroupName,",
                join: "LEFT JOIN tbl_Item t3 ON t2.fk_ItemID = t3.pk_ItemID LEFT JOIN tbl_Brands br ON t3.fk_BrandID = br.pk_BrandID",
                field: "CAST(ISNULL(t3.fk_BrandID, 0) AS NVARCHAR(20)), ISNULL(br.BrandDesc, N'N/A')",
                groupCodeOnly: "CAST(ISNULL(t3.fk_BrandID, 0) AS NVARCHAR(20))",
                orderBy: "cy.GroupName",
                hasGrouping: true
            ),
            _ => ("", "", "", "", "", false)
        };
    }

    private (string whereClause, List<SqlParameter> parameters) BuildStoreFilter(List<string>? storeCodes)
    {
        if (storeCodes == null || !storeCodes.Any())
            return ("", new List<SqlParameter>());

        var parameters = new List<SqlParameter>();
        var paramNames = new List<string>();
        
        for (int i = 0; i < storeCodes.Count; i++)
        {
            var paramName = $"@store{i}";
            paramNames.Add(paramName);
            parameters.Add(new SqlParameter(paramName, storeCodes[i]));
        }
        
        var whereClause = $" AND t1.fk_StoreCode IN ({string.Join(", ", paramNames)})";
        return (whereClause, parameters);
    }

    private string BuildMainQuery(
        string periodField, 
        (string select, string join, string field, string groupCodeOnly, string orderBy, bool hasGrouping) grouping,
        (string whereClause, List<SqlParameter> parameters) storeFilter,
        ReportFilter filter)
    {
        var hasGrouping = grouping.hasGrouping;
        var includeLastYear = filter.CompareLastYear;
        var storeWhere = storeFilter.whereClause;
        
        if (!hasGrouping)
            return BuildMainQueryNoGrouping(periodField, storeWhere, filter, includeLastYear);
        
        string groupSelectClause = grouping.select;
        string groupJoinClause = grouping.join;
        string groupByClause = $", {grouping.field}";
        
        string lyJoinCondition = "ly.Period = CAST(CAST(SUBSTRING(cy.Period, 1, 4) AS INT) - 1 AS VARCHAR(4)) + SUBSTRING(cy.Period, 5, 20)";
        string lyGroupMatch = " AND ly.GroupCode = cy.GroupCode";
        
        // Build Sales CTE (with grouping)
        var salesCte = $@"
            SELECT 
                {periodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            {groupJoinClause}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
            GROUP BY {periodField}{groupByClause}";
        
        // Build Returns CTE
        var creditJoin = groupJoinClause.Replace("t2.fk_Invoice", "t2.fk_Credit");
        var returnsCte = $@"
            SELECT 
                {periodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Quantity) AS QtyReturned,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            {creditJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
            GROUP BY {periodField}{groupByClause}";
        
        // Build CY CTE (hasGrouping is true here)
        var cyJoinCondition = "s.Period = r.Period AND s.GroupCode = r.GroupCode";
        var cyCte = $@"
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
            FULL OUTER JOIN Returns r ON {cyJoinCondition}";
        
        // Build LY CTEs if needed
        var lyCtes = "";
        var lyJoin = "";
        var lySelectColumns = "";
        
        if (includeLastYear)
        {
            var lySalesCte = $@"
            SELECT 
                {periodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            {groupJoinClause}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}
            GROUP BY {periodField}{groupByClause}";
            
            var lyReturnsCte = $@"
            SELECT 
                {periodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            {creditJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}
            GROUP BY {periodField}{groupByClause}";
            
            var lyMergeCte = $@"
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
            FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period AND lys.GroupCode = lyr.GroupCode";
            
            lyCtes = $@",
                LYSales AS ({lySalesCte}),
                LYReturns AS ({lyReturnsCte}),
                LY AS ({lyMergeCte})";
            
            lyJoin = $@"
                LEFT JOIN LY ly ON {lyJoinCondition}{lyGroupMatch}";
            
            lySelectColumns = @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.NetSales, 0) AS LYNetSales,
                    ISNULL(ly.NetReturns, 0) AS LYNetReturns,
                    ISNULL(ly.VatSales, 0) AS LYVatSales,
                    ISNULL(ly.VatReturns, 0) AS LYVatReturns";
        }
        
        // Build final SELECT with pagination
        var orderBy = $"{grouping.orderBy}, cy.Period";
        
        var sql = $@"
            ;WITH Sales AS ({salesCte}),
                Returns AS ({returnsCte}),
                CY AS ({cyCte}){lyCtes}
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
                cy.VatReturns{lySelectColumns}
            FROM CY cy
            {lyJoin}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY";
        
        return sql;
    }

    private string BuildMainQueryNoGrouping(string periodField, string storeWhere, ReportFilter filter, bool includeLastYear)
    {
        var lyCtes = "";
        var lyJoin = "";
        var lySelectColumns = "";

        var salesCte = $@"
            SELECT 
                {periodField} AS Period,
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
            GROUP BY {periodField}";

        var returnsCte = $@"
            SELECT 
                {periodField} AS Period,
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Quantity) AS QtyReturned,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
            GROUP BY {periodField}";

        var cyCte = @"
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
            FULL OUTER JOIN Returns r ON s.Period = r.Period";

        if (includeLastYear)
        {
            var lyJoinCondition = "ly.Period = CAST(CAST(SUBSTRING(cy.Period, 1, 4) AS INT) - 1 AS VARCHAR(4)) + SUBSTRING(cy.Period, 5, 20)";
            lyCtes = $@",
                LYSales AS (
            SELECT {periodField} AS Period,
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}
            GROUP BY {periodField}
                ),
                LYReturns AS (
            SELECT {periodField} AS Period,
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}
            GROUP BY {periodField}
                ),
                LY AS (
            SELECT COALESCE(lys.Period, lyr.Period) AS Period,
                ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(lyr.CreditCount, 0) AS CreditCount,
                ISNULL(lys.NetSales, 0) AS NetSales,
                ISNULL(lyr.NetReturns, 0) AS NetReturns,
                ISNULL(lys.VatSales, 0) AS VatSales,
                ISNULL(lyr.VatReturns, 0) AS VatReturns
            FROM LYSales lys
            FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period
                )";
            lyJoin = $@"
                LEFT JOIN LY ly ON {lyJoinCondition}";
            lySelectColumns = @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.NetSales, 0) AS LYNetSales,
                    ISNULL(ly.NetReturns, 0) AS LYNetReturns,
                    ISNULL(ly.VatSales, 0) AS LYVatSales,
                    ISNULL(ly.VatReturns, 0) AS LYVatReturns";
        }

        return $@"
            ;WITH Sales AS ({salesCte}),
                Returns AS ({returnsCte}),
                CY AS ({cyCte}){lyCtes}
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
                cy.VatReturns{lySelectColumns}
            FROM CY cy
            {lyJoin}
            ORDER BY cy.Period
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY";
    }

    private string BuildCountQuery(
        string periodField,
        (string select, string join, string field, string groupCodeOnly, string orderBy, bool hasGrouping) grouping,
        (string whereClause, List<SqlParameter> parameters) storeFilter,
        ReportFilter filter)
    {
        var storeWhere = storeFilter.whereClause;

        if (!grouping.hasGrouping)
            return BuildCountQueryNoGrouping(periodField, storeWhere);

        var groupJoinClause = grouping.join;
        var groupCodeExpr = grouping.groupCodeOnly;
        var groupByClause = $", {grouping.field}";
        var creditJoin = groupJoinClause.Replace("t2.fk_Invoice", "t2.fk_Credit");

        return $@"
;WITH Sales AS (
    SELECT {periodField} AS Period, {groupCodeExpr} AS GroupCode
    FROM tbl_InvoiceHeader t1
    INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
    {groupJoinClause}
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
    GROUP BY {periodField}{groupByClause}
),
Returns AS (
    SELECT {periodField} AS Period, {groupCodeExpr} AS GroupCode
    FROM tbl_CreditHeader t1
    INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
    {creditJoin}
    WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
    GROUP BY {periodField}{groupByClause}
),
CY AS (
    SELECT COALESCE(s.Period, r.Period) AS Period
    FROM Sales s
    FULL OUTER JOIN Returns r ON s.Period = r.Period AND s.GroupCode = r.GroupCode
)
SELECT COUNT(*) FROM CY";
    }

    private string BuildCountQueryNoGrouping(string periodField, string storeWhere)
    {
        return $@"
            ;WITH Sales AS (
                SELECT {periodField} AS Period
                FROM tbl_InvoiceHeader t1
                INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
                GROUP BY {periodField}
            ),
            Returns AS (
                SELECT {periodField} AS Period
                FROM tbl_CreditHeader t1
                INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}
                GROUP BY {periodField}
            ),
            CY AS (
                SELECT COALESCE(s.Period, r.Period) AS Period
                FROM Sales s FULL OUTER JOIN Returns r ON s.Period = r.Period
            )
            SELECT COUNT(*) FROM CY";
    }

    #endregion

    #region Query Execution

    private async Task<int> GetTotalCountAsync(SqlConnection conn, string countSql, ReportFilter filter)
    {
        using var cmd = new SqlCommand(countSql, conn);
        AddCommonParameters(cmd, filter);
        
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<List<AverageBasketRow>> ExecuteMainQueryAsync(
        SqlConnection conn, 
        string sql, 
        ReportFilter filter, 
        bool hasGrouping)
    {
        var results = new List<AverageBasketRow>();
        
        using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, filter);
        cmd.Parameters.AddWithValue("@Skip", filter.Skip);
        cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var hasLy = filter.CompareLastYear;
        
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

    private void AddCommonParameters(SqlCommand cmd, ReportFilter filter)
    {
        cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", filter.DateTo.Date);
        
        // Add store filter parameters
        for (int i = 0; i < filter.StoreCodes.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@store{i}", filter.StoreCodes[i]);
        }
    }

    #endregion

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            
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
