using System.Globalization;
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
        var grouping = BuildCombinedGrouping(filter.GroupBy, filter.SecondaryGroupBy);
        var storeFilter = BuildStoreFilter(filter.StoreCodes);
        var itemFilter = BuildItemFilter(filter.ItemIds);
        bool anyGrouping = grouping.hasLevel1 || grouping.hasLevel2;
        
        var (ctes, dataSelect) = anyGrouping
            ? BuildQueryPartsGrouped(periodField, grouping, storeFilter, itemFilter, filter)
            : BuildQueryPartsNoGrouping(periodField, storeFilter.whereClause, itemFilter.whereClause, filter);
        
        var (filterWhere, filterParams) = BuildColumnFilterClause(filter);
        var sortExpr = ResolveSortExpression(filter, anyGrouping);
        
        var sql = $@"{ctes},
            Data AS ({dataSelect})
            SELECT * FROM Data d{filterWhere}
            ORDER BY {sortExpr}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY";
        
        var countSql = $@"{ctes},
            Data AS ({dataSelect})
            SELECT COUNT(*) FROM Data d{filterWhere}";
        
        var totalsSql = BuildGrandTotalsQuery(storeFilter, itemFilter, filter);
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        int totalCount = await GetTotalCountAsync(conn, countSql, filter, filterParams);
        var results = await ExecuteMainQueryAsync(conn, sql, filter, grouping.hasLevel1, grouping.hasLevel2, filterParams);
        var grandTotals = await ExecuteGrandTotalsQueryAsync(conn, totalsSql, filter);
        
        return new PagedResult<AverageBasketRow>
        {
            Items = results,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            GrandTotals = grandTotals
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
            PageSize = int.MaxValue
        };
        
        var result = await GetAverageBasketDataAsync(filter);
        return result.Items;
    }

    #region Grouping Helpers

    private record GroupingFragment(
        string SelectClause,
        string JoinClause,
        string GroupByFields,
        string CodeOnly
    );

    private GroupingFragment? GetGroupingFragment(GroupByType type, int level)
    {
        if (type == GroupByType.None) return null;

        string itemAlias = level == 1 ? "t3" : "t5";
        string suffix = level == 1 ? "" : "2";
        string codeAlias = $"Group{suffix}Code";
        string nameAlias = $"Group{suffix}Name";

        return type switch
        {
            GroupByType.Store => new GroupingFragment(
                $"t1.fk_StoreCode AS {codeAlias}, ISNULL(st{suffix}.StoreName, t1.fk_StoreCode) AS {nameAlias},",
                $"LEFT JOIN tbl_Store st{suffix} ON t1.fk_StoreCode = st{suffix}.pk_StoreCode",
                $"t1.fk_StoreCode, ISNULL(st{suffix}.StoreName, t1.fk_StoreCode)",
                "t1.fk_StoreCode"),

            GroupByType.Category => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20)) AS {codeAlias}, ISNULL(cat{suffix}.CategoryDescr, 'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_ItemCategory cat{suffix} ON {itemAlias}.fk_CategoryID = cat{suffix}.pk_CategoryID",
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20)), ISNULL(cat{suffix}.CategoryDescr, 'N/A')",
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20))"),

            GroupByType.Department => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20)) AS {codeAlias}, ISNULL(dep{suffix}.DepartmentDescr, N'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_ItemDepartment dep{suffix} ON {itemAlias}.fk_DepartmentID = dep{suffix}.pk_DepartmentID",
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20)), ISNULL(dep{suffix}.DepartmentDescr, N'N/A')",
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20))"),

            GroupByType.Brand => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20)) AS {codeAlias}, ISNULL(br{suffix}.BrandDesc, N'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Brands br{suffix} ON {itemAlias}.fk_BrandID = br{suffix}.pk_BrandID",
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20)), ISNULL(br{suffix}.BrandDesc, N'N/A')",
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20))"),

            _ => null
        };
    }

    private (string select, string join, string groupByFields, string codeOnly, bool hasLevel1, bool hasLevel2) 
        BuildCombinedGrouping(GroupByType primary, GroupByType secondary)
    {
        var g1 = GetGroupingFragment(primary, 1);
        var g2 = GetGroupingFragment(secondary, 2);

        if (g1 == null && g2 == null)
            return ("", "", "", "", false, false);

        var selectParts = new List<string>();
        var joinParts = new List<string>();
        var groupByParts = new List<string>();
        var codeParts = new List<string>();

        if (g1 != null)
        {
            selectParts.Add(g1.SelectClause);
            joinParts.Add(g1.JoinClause);
            groupByParts.Add(g1.GroupByFields);
            codeParts.Add(g1.CodeOnly);
        }
        else
        {
            selectParts.Add("NULL AS GroupCode, NULL AS GroupName,");
        }

        if (g2 != null)
        {
            selectParts.Add(g2.SelectClause);
            joinParts.Add(g2.JoinClause);
            groupByParts.Add(g2.GroupByFields);
            codeParts.Add(g2.CodeOnly);
        }
        else
        {
            selectParts.Add("NULL AS Group2Code, NULL AS Group2Name,");
        }

        return (
            string.Join("\n                ", selectParts),
            string.Join("\n            ", joinParts),
            string.Join(", ", groupByParts),
            string.Join(", ", codeParts),
            g1 != null,
            g2 != null
        );
    }

    #endregion

    #region SQL Building

    private static readonly Dictionary<string, string> ColumnSqlMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Period"] = "d.Period",
        ["GroupName"] = "d.GroupName",
        ["Group2Name"] = "d.Group2Name",
        ["Invoices"] = "d.InvoiceCount",
        ["Returns"] = "d.CreditCount",
        ["NetTransactions"] = "(d.InvoiceCount - d.CreditCount)",
        ["QtySold"] = "d.QtySold",
        ["QtyReturned"] = "d.QtyReturned",
        ["NetQty"] = "(d.QtySold - d.QtyReturned)",
        ["Sales"] = "(d.NetSales - d.NetReturns)",
        ["AvgBasket"] = "CASE WHEN (d.InvoiceCount - d.CreditCount) > 0 THEN (d.NetSales - d.NetReturns) * 1.0 / (d.InvoiceCount - d.CreditCount) ELSE 0 END",
        ["AvgQty"] = "CASE WHEN (d.InvoiceCount - d.CreditCount) > 0 THEN CAST((d.QtySold - d.QtyReturned) AS FLOAT) / (d.InvoiceCount - d.CreditCount) ELSE 0 END"
    };

    private static readonly HashSet<string> TextColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Period", "GroupName", "Group2Name"
    };

    private static readonly HashSet<string> ValidFilterOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "neq", "lt", "lte", "gt", "gte", "contains", "starts", "ends"
    };

    private string ResolveSortExpression(ReportFilter filter, bool hasGrouping)
    {
        var col = filter.SortColumn ?? "Period";
        var dir = string.Equals(filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        if (!ColumnSqlMap.TryGetValue(col, out var sqlExpr))
            sqlExpr = "d.Period";

        if (col == "GroupName" && !hasGrouping)
            sqlExpr = "d.Period";

        return $"{sqlExpr} {dir}";
    }

    private (string whereClause, List<SqlParameter> filterParams) BuildColumnFilterClause(ReportFilter filter)
    {
        if (filter.FilterValues == null || !filter.FilterValues.Any())
            return ("", new List<SqlParameter>());

        var conditions = new List<string>();
        var parameters = new List<SqlParameter>();
        int idx = 0;

        foreach (var kvp in filter.FilterValues)
        {
            var column = kvp.Key;
            var value = kvp.Value?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            if (!ColumnSqlMap.TryGetValue(column, out var sqlExpr)) continue;

            var op = filter.FilterOperators.TryGetValue(column, out var opVal) ? opVal : "eq";
            if (!ValidFilterOps.Contains(op)) op = "eq";

            var paramName = $"@cf{idx++}";
            bool isText = TextColumns.Contains(column);

            switch (op)
            {
                case "contains":
                    conditions.Add($"CAST({sqlExpr} AS NVARCHAR(200)) LIKE '%' + {paramName} + '%'");
                    parameters.Add(new SqlParameter(paramName, value));
                    break;
                case "starts":
                    conditions.Add($"CAST({sqlExpr} AS NVARCHAR(200)) LIKE {paramName} + '%'");
                    parameters.Add(new SqlParameter(paramName, value));
                    break;
                case "ends":
                    conditions.Add($"CAST({sqlExpr} AS NVARCHAR(200)) LIKE '%' + {paramName}");
                    parameters.Add(new SqlParameter(paramName, value));
                    break;
                default:
                {
                    var sqlOp = op switch
                    {
                        "neq" => "<>",
                        "lt" => "<",
                        "lte" => "<=",
                        "gt" => ">",
                        "gte" => ">=",
                        _ => "="
                    };
                    if (isText)
                    {
                        conditions.Add($"{sqlExpr} {sqlOp} {paramName}");
                        parameters.Add(new SqlParameter(paramName, value));
                    }
                    else
                    {
                        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
                        {
                            conditions.Add($"{sqlExpr} {sqlOp} {paramName}");
                            parameters.Add(new SqlParameter(paramName, numVal));
                        }
                    }
                    break;
                }
            }
        }

        if (!conditions.Any())
            return ("", new List<SqlParameter>());

        return ($"\n            WHERE {string.Join(" AND ", conditions)}", parameters);
    }

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

    private (string whereClause, List<SqlParameter> parameters) BuildItemFilter(List<int>? itemIds)
    {
        if (itemIds == null || !itemIds.Any())
            return ("", new List<SqlParameter>());

        var parameters = new List<SqlParameter>();
        var paramNames = new List<string>();
        
        for (int i = 0; i < itemIds.Count; i++)
        {
            var paramName = $"@item{i}";
            paramNames.Add(paramName);
            parameters.Add(new SqlParameter(paramName, itemIds[i]));
        }
        
        var whereClause = $" AND t2.fk_ItemID IN ({string.Join(", ", paramNames)})";
        return (whereClause, parameters);
    }

    /// <summary>
    /// Returns (cteDefinitions, dataSelectClause) for grouped queries.
    /// Caller wraps dataSelect in a Data CTE and appends ORDER BY / OFFSET.
    /// </summary>
    private (string ctes, string dataSelect) BuildQueryPartsGrouped(
        string periodField,
        (string select, string join, string groupByFields, string codeOnly, bool hasLevel1, bool hasLevel2) grouping,
        (string whereClause, List<SqlParameter> parameters) storeFilter,
        (string whereClause, List<SqlParameter> parameters) itemFilter,
        ReportFilter filter)
    {
        var storeWhere = storeFilter.whereClause;
        var itemWhere = itemFilter.whereClause;
        var includeLastYear = filter.CompareLastYear;
        bool hasL2 = grouping.hasLevel2;

        var groupSelectClause = grouping.select;
        var groupJoinClause = grouping.join;
        var groupByClause = $", {grouping.groupByFields}";

        var cyJoin = "s.Period = r.Period AND s.GroupCode = r.GroupCode";
        if (hasL2) cyJoin += " AND s.Group2Code = r.Group2Code";

        var lyJoinBase = "ly.Period = CAST(CAST(SUBSTRING(cy.Period, 1, 4) AS INT) - 1 AS VARCHAR(4)) + SUBSTRING(cy.Period, 5, 20) AND ly.GroupCode = cy.GroupCode";
        if (hasL2) lyJoinBase += " AND ly.Group2Code = cy.Group2Code";

        var g2CySelect = hasL2
            ? "COALESCE(s.Group2Code, r.Group2Code) AS Group2Code,\n                COALESCE(s.Group2Name, r.Group2Name) AS Group2Name,"
            : "NULL AS Group2Code, NULL AS Group2Name,";

        var g2FinalSelect = ",\n                cy.Group2Code, cy.Group2Name";

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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
            GROUP BY {periodField}{groupByClause}";

        var creditJoin = groupJoinClause
            .Replace("t2.fk_Invoice", "t2.fk_Credit");

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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
            GROUP BY {periodField}{groupByClause}";

        var cyCte = $@"
            SELECT 
                COALESCE(s.Period, r.Period) AS Period,
                COALESCE(s.GroupCode, r.GroupCode) AS GroupCode,
                COALESCE(s.GroupName, r.GroupName) AS GroupName,
                {g2CySelect}
                ISNULL(s.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(r.CreditCount, 0) AS CreditCount,
                ISNULL(s.QtySold, 0) AS QtySold,
                ISNULL(r.QtyReturned, 0) AS QtyReturned,
                ISNULL(s.NetSales, 0) AS NetSales,
                ISNULL(r.NetReturns, 0) AS NetReturns,
                ISNULL(s.VatSales, 0) AS VatSales,
                ISNULL(r.VatReturns, 0) AS VatReturns
            FROM Sales s
            FULL OUTER JOIN Returns r ON {cyJoin}";

        var lyCtes = "";
        var lyJoin = "";
        var lySelectColumns = "";

        if (includeLastYear)
        {
            var g2LyCySelect = hasL2
                ? "COALESCE(lys.Group2Code, lyr.Group2Code) AS Group2Code,\n                COALESCE(lys.Group2Name, lyr.Group2Name) AS Group2Name,"
                : "";

            var lyMergeJoin = "lys.Period = lyr.Period AND lys.GroupCode = lyr.GroupCode";
            if (hasL2) lyMergeJoin += " AND lys.Group2Code = lyr.Group2Code";

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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
            GROUP BY {periodField}{groupByClause}";

            var lyMergeCte = $@"
            SELECT 
                COALESCE(lys.Period, lyr.Period) AS Period,
                COALESCE(lys.GroupCode, lyr.GroupCode) AS GroupCode,
                COALESCE(lys.GroupName, lyr.GroupName) AS GroupName,
                {g2LyCySelect}
                ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(lyr.CreditCount, 0) AS CreditCount,
                ISNULL(lys.NetSales, 0) AS NetSales,
                ISNULL(lyr.NetReturns, 0) AS NetReturns,
                ISNULL(lys.VatSales, 0) AS VatSales,
                ISNULL(lyr.VatReturns, 0) AS VatReturns
            FROM LYSales lys
            FULL OUTER JOIN LYReturns lyr ON {lyMergeJoin}";

            lyCtes = $@",
                LYSales AS ({lySalesCte}),
                LYReturns AS ({lyReturnsCte}),
                LY AS ({lyMergeCte})";

            lyJoin = $"\n                LEFT JOIN LY ly ON {lyJoinBase}";

            lySelectColumns = @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.NetSales, 0) AS LYNetSales,
                    ISNULL(ly.NetReturns, 0) AS LYNetReturns,
                    ISNULL(ly.VatSales, 0) AS LYVatSales,
                    ISNULL(ly.VatReturns, 0) AS LYVatReturns";
        }

        var ctes = $@"
            ;WITH Sales AS ({salesCte}),
                Returns AS ({returnsCte}),
                CY AS ({cyCte}){lyCtes}";

        var dataSelect = $@"
            SELECT 
                cy.Period,
                cy.GroupCode,
                cy.GroupName{g2FinalSelect},
                cy.InvoiceCount,
                cy.CreditCount,
                cy.QtySold,
                cy.QtyReturned,
                cy.NetSales,
                cy.NetReturns,
                cy.VatSales,
                cy.VatReturns{lySelectColumns}
            FROM CY cy
            {lyJoin}";

        return (ctes, dataSelect);
    }

    /// <summary>
    /// Returns (cteDefinitions, dataSelectClause) for non-grouped queries.
    /// </summary>
    private (string ctes, string dataSelect) BuildQueryPartsNoGrouping(
        string periodField, string storeWhere, string itemWhere, ReportFilter filter)
    {
        var includeLastYear = filter.CompareLastYear;
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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
            GROUP BY {periodField}";

        var cyCte = @"
            SELECT 
                COALESCE(s.Period, r.Period) AS Period,
                NULL AS GroupCode, NULL AS GroupName,
                NULL AS Group2Code, NULL AS Group2Name,
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
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
            GROUP BY {periodField}
                ),
                LYReturns AS (
            SELECT {periodField} AS Period,
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
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

        var ctes = $@"
            ;WITH Sales AS ({salesCte}),
                Returns AS ({returnsCte}),
                CY AS ({cyCte}){lyCtes}";

        var dataSelect = $@"
            SELECT 
                cy.Period,
                cy.GroupCode, cy.GroupName,
                cy.Group2Code, cy.Group2Name,
                cy.InvoiceCount,
                cy.CreditCount,
                cy.QtySold,
                cy.QtyReturned,
                cy.NetSales,
                cy.NetReturns,
                cy.VatSales,
                cy.VatReturns{lySelectColumns}
            FROM CY cy
            {lyJoin}";

        return (ctes, dataSelect);
    }

    private string BuildGrandTotalsQuery(
        (string whereClause, List<SqlParameter> parameters) storeFilter,
        (string whereClause, List<SqlParameter> parameters) itemFilter,
        ReportFilter filter)
    {
        var storeWhere = storeFilter.whereClause;
        var itemWhere = itemFilter.whereClause;
        var includeLastYear = filter.CompareLastYear;

        var lyColumns = "";
        var lySubQueries = "";

        if (includeLastYear)
        {
            lyColumns = @",
                ISNULL(LYI.LYInvoiceCount, 0) AS LYInvoiceCount,
                ISNULL(LYC.LYCreditCount, 0) AS LYCreditCount,
                ISNULL(LYI.LYNetSales, 0) AS LYNetSales,
                ISNULL(LYC.LYNetReturns, 0) AS LYNetReturns,
                ISNULL(LYI.LYVatSales, 0) AS LYVatSales,
                ISNULL(LYC.LYVatReturns, 0) AS LYVatReturns";

            lySubQueries = $@"
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_InvoiceID) AS LYInvoiceCount,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS LYNetSales,
                       SUM(ISNULL(t2.VatAmount, 0)) AS LYVatSales
                FROM tbl_InvoiceHeader t1
                INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
            ) LYI ON 1=1
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_CreditID) AS LYCreditCount,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS LYNetReturns,
                       SUM(ISNULL(t2.VatAmount, 0)) AS LYVatReturns
                FROM tbl_CreditHeader t1
                INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}
            ) LYC ON 1=1";
        }

        return $@"
            SELECT 
                ISNULL(SI.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(CR.CreditCount, 0) AS CreditCount,
                ISNULL(SI.QtySold, 0) AS QtySold,
                ISNULL(CR.QtyReturned, 0) AS QtyReturned,
                ISNULL(SI.NetSales, 0) AS NetSales,
                ISNULL(CR.NetReturns, 0) AS NetReturns,
                ISNULL(SI.VatSales, 0) AS VatSales,
                ISNULL(CR.VatReturns, 0) AS VatReturns{lyColumns}
            FROM (
                SELECT COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                       SUM(t2.Quantity) AS QtySold,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                       SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
                FROM tbl_InvoiceHeader t1
                INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
            ) SI
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                       SUM(t2.Quantity) AS QtyReturned,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                       SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                FROM tbl_CreditHeader t1
                INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}
            ) CR ON 1=1{lySubQueries}";
    }

    #endregion

    #region Query Execution

    private async Task<int> GetTotalCountAsync(
        SqlConnection conn, string countSql, ReportFilter filter, List<SqlParameter> filterParams)
    {
        using var cmd = new SqlCommand(countSql, conn);
        AddCommonParameters(cmd, filter);
        AddFilterParameters(cmd, filterParams);
        
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<List<AverageBasketRow>> ExecuteMainQueryAsync(
        SqlConnection conn, 
        string sql, 
        ReportFilter filter, 
        bool hasLevel1,
        bool hasLevel2,
        List<SqlParameter> filterParams)
    {
        var results = new List<AverageBasketRow>();
        
        using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, filter);
        AddFilterParameters(cmd, filterParams);
        cmd.Parameters.AddWithValue("@Skip", filter.Skip);
        cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var hasLy = filter.CompareLastYear;
        
        // Column layout: Period(0), GroupCode(1), GroupName(2), Group2Code(3), Group2Name(4),
        // InvoiceCount(5), CreditCount(6), QtySold(7), QtyReturned(8),
        // NetSales(9), NetReturns(10), VatSales(11), VatReturns(12)
        // [LY columns start at 13 if present]
        
        while (await reader.ReadAsync())
        {
            var row = new AverageBasketRow
            {
                Period = reader.GetString(0),
                Level1 = reader.IsDBNull(1) ? null : reader.GetString(1),
                Level1Value = reader.IsDBNull(2) ? null : reader.GetString(2),
                Level2 = reader.IsDBNull(3) ? null : reader.GetString(3),
                Level2Value = reader.IsDBNull(4) ? null : reader.GetString(4),
                CYInvoiceCount = reader.GetInt32(5),
                CYCreditCount = reader.GetInt32(6),
                CYQtySold = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                CYQtyReturned = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                CYNetSales = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                CYNetReturns = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10),
                CYVatSales = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11),
                CYVatReturns = reader.IsDBNull(12) ? 0 : reader.GetDecimal(12)
            };
            
            row.CYGrossSales = row.CYNetSales + row.CYVatSales;
            row.CYGrossReturns = row.CYNetReturns + row.CYVatReturns;
            
            if (hasLy)
            {
                row.LYInvoiceCount = reader.GetInt32(13);
                row.LYCreditCount = reader.GetInt32(14);
                row.LYNetSales = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15);
                row.LYNetReturns = reader.IsDBNull(16) ? 0 : reader.GetDecimal(16);
                row.LYVatSales = reader.IsDBNull(17) ? 0 : reader.GetDecimal(17);
                row.LYVatReturns = reader.IsDBNull(18) ? 0 : reader.GetDecimal(18);
                row.LYTotalNet = row.LYNetSales - row.LYNetReturns;
                row.LYTotalGross = row.LYTotalNet + (row.LYVatSales - row.LYVatReturns);
            }
            
            results.Add(row);
        }
        
        return results;
    }

    private async Task<ReportGrandTotals> ExecuteGrandTotalsQueryAsync(SqlConnection conn, string sql, ReportFilter filter)
    {
        var totals = new ReportGrandTotals();
        
        using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, filter);
        
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totals.TotalInvoices = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            totals.TotalCredits = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            totals.TotalQtySold = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
            totals.TotalQtyReturned = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
            totals.TotalNetSales = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
            totals.TotalNetReturns = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
            totals.TotalVatSales = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
            totals.TotalVatReturns = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7);
            
            if (filter.CompareLastYear && reader.FieldCount > 8)
            {
                totals.LYTotalInvoices = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
                totals.LYTotalCredits = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);
                totals.LYNetSales = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10);
                totals.LYNetReturns = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11);
                totals.LYVatSales = reader.IsDBNull(12) ? 0 : reader.GetDecimal(12);
                totals.LYVatReturns = reader.IsDBNull(13) ? 0 : reader.GetDecimal(13);
            }
        }
        
        return totals;
    }

    private void AddCommonParameters(SqlCommand cmd, ReportFilter filter)
    {
        cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", filter.DateTo.Date);
        
        for (int i = 0; i < filter.StoreCodes.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@store{i}", filter.StoreCodes[i]);
        }
        
        for (int i = 0; i < filter.ItemIds.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@item{i}", filter.ItemIds[i]);
        }
    }

    private void AddFilterParameters(SqlCommand cmd, List<SqlParameter> filterParams)
    {
        foreach (var p in filterParams)
            cmd.Parameters.AddWithValue(p.ParameterName, p.Value);
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
