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

    private static readonly DimensionFilterBuilder.ColumnMap AbDimCols = new(
        Category: "t_dim.fk_CategoryID",
        Department: "t_dim.fk_DepartmentID",
        Brand: "t_dim.fk_BrandID",
        Season: "t_dim.fk_SeasonID",
        Item: "t2.fk_ItemID",
        Store: "t1.fk_StoreCode",
        ItemTableAlias: "t_dim");

    public async Task<PagedResult<AverageBasketRow>> GetAverageBasketDataAsync(ReportFilter filter)
    {
        var periodField = GetPeriodField(filter.Breakdown);
        var grouping = BuildCombinedGrouping(filter.GroupBy, filter.SecondaryGroupBy);
        var storeFilter = BuildStoreFilter(filter.StoreCodes);
        var itemFilter = BuildItemFilter(filter.ItemIds);
        bool anyGrouping = grouping.hasLevel1 || grouping.hasLevel2;

        var needsDimJoin = DimensionFilterBuilder.NeedsItemJoin(filter.ItemsSelection);
        var dimJoin = needsDimJoin ? "\n            INNER JOIN tbl_Item t_dim ON t2.fk_ItemID = t_dim.pk_ItemID" : "";
        var (dimWhere, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection, AbDimCols);

        var (ctes, dataSelect) = anyGrouping
            ? BuildQueryPartsGrouped(periodField, grouping, storeFilter, itemFilter, filter, dimJoin, dimWhere)
            : BuildQueryPartsNoGrouping(periodField, storeFilter.whereClause, itemFilter.whereClause, filter, dimJoin, dimWhere);
        
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
        
        var totalsSql = BuildGrandTotalsQuery(storeFilter, itemFilter, filter, dimJoin, dimWhere);
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        int totalCount = await GetTotalCountAsync(conn, countSql, filter, filterParams, dimParams);
        var results = await ExecuteMainQueryAsync(conn, sql, filter, grouping.hasLevel1, grouping.hasLevel2, filterParams, dimParams);
        var grandTotals = await ExecuteGrandTotalsQueryAsync(conn, totalsSql, filter, dimParams);
        
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
                $"t1.fk_StoreCode AS {codeAlias}, CASE WHEN ISNULL(st{suffix}.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st{suffix}.StoreName, t1.fk_StoreCode))) END AS {nameAlias}, ISNULL(st{suffix}.StoreArea, 0) AS StoreArea,",
                $"LEFT JOIN tbl_Store st{suffix} ON t1.fk_StoreCode = st{suffix}.pk_StoreCode",
                $"t1.fk_StoreCode, CASE WHEN ISNULL(st{suffix}.pk_StoreCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(t1.fk_StoreCode)) + N' - ' + LTRIM(RTRIM(ISNULL(st{suffix}.StoreName, t1.fk_StoreCode))) END, ISNULL(st{suffix}.StoreArea, 0)",
                "t1.fk_StoreCode"),

            GroupByType.Category => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(cat{suffix}.CategoryCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(cat{suffix}.CategoryCode)) + N' - ' + LTRIM(RTRIM(cat{suffix}.CategoryDescr)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_ItemCategory cat{suffix} ON {itemAlias}.fk_CategoryID = cat{suffix}.pk_CategoryID",
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(cat{suffix}.CategoryCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(cat{suffix}.CategoryCode)) + N' - ' + LTRIM(RTRIM(cat{suffix}.CategoryDescr)) END",
                $"CAST(ISNULL({itemAlias}.fk_CategoryID, 0) AS NVARCHAR(20))"),

            GroupByType.Department => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(dep{suffix}.DepartmentCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(dep{suffix}.DepartmentCode)) + N' - ' + LTRIM(RTRIM(dep{suffix}.DepartmentDescr)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_ItemDepartment dep{suffix} ON {itemAlias}.fk_DepartmentID = dep{suffix}.pk_DepartmentID",
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(dep{suffix}.DepartmentCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(dep{suffix}.DepartmentCode)) + N' - ' + LTRIM(RTRIM(dep{suffix}.DepartmentDescr)) END",
                $"CAST(ISNULL({itemAlias}.fk_DepartmentID, 0) AS NVARCHAR(20))"),

            GroupByType.Brand => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(br{suffix}.BrandCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(br{suffix}.BrandCode)) + N' - ' + LTRIM(RTRIM(br{suffix}.BrandDesc)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Brands br{suffix} ON {itemAlias}.fk_BrandID = br{suffix}.pk_BrandID",
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(br{suffix}.BrandCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(br{suffix}.BrandCode)) + N' - ' + LTRIM(RTRIM(br{suffix}.BrandDesc)) END",
                $"CAST(ISNULL({itemAlias}.fk_BrandID, 0) AS NVARCHAR(20))"),

            GroupByType.Season => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_SeasonID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(sea{suffix}.SeasonCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(sea{suffix}.SeasonCode)) + N' - ' + LTRIM(RTRIM(sea{suffix}.SeasonDesc)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Season sea{suffix} ON {itemAlias}.fk_SeasonID = sea{suffix}.pk_SeasonID",
                $"CAST(ISNULL({itemAlias}.fk_SeasonID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(sea{suffix}.SeasonCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(sea{suffix}.SeasonCode)) + N' - ' + LTRIM(RTRIM(sea{suffix}.SeasonDesc)) END",
                $"CAST(ISNULL({itemAlias}.fk_SeasonID, 0) AS NVARCHAR(20))"),

            GroupByType.Customer => new GroupingFragment(
                $"ISNULL(cust{suffix}.pk_CustomerNo, N'') AS {codeAlias}, ISNULL(CASE WHEN cust{suffix}.Company = 1 THEN cust{suffix}.LastCompanyName ELSE cust{suffix}.FirstName + N' ' + cust{suffix}.LastCompanyName END, N'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_Customer cust{suffix} ON t1.fk_CustomerCode = cust{suffix}.pk_CustomerNo",
                $"ISNULL(cust{suffix}.pk_CustomerNo, N''), ISNULL(CASE WHEN cust{suffix}.Company = 1 THEN cust{suffix}.LastCompanyName ELSE cust{suffix}.FirstName + N' ' + cust{suffix}.LastCompanyName END, N'N/A')",
                $"ISNULL(cust{suffix}.pk_CustomerNo, N'')"),

            GroupByType.User => new GroupingFragment(
                $"ISNULL(t1.fk_UserCode, N'') AS {codeAlias}, ISNULL(t1.UserName, ISNULL(t1.fk_UserCode, N'N/A')) AS {nameAlias},",
                "",
                $"ISNULL(t1.fk_UserCode, N''), ISNULL(t1.UserName, ISNULL(t1.fk_UserCode, N'N/A'))",
                $"ISNULL(t1.fk_UserCode, N'')"),

            GroupByType.Supplier => new GroupingFragment(
                $"ISNULL(sup{suffix}.pk_SupplierNo, N'') AS {codeAlias}, ISNULL(CASE WHEN sup{suffix}.Company = 1 THEN sup{suffix}.LastCompanyName ELSE sup{suffix}.FirstName + N' ' + sup{suffix}.LastCompanyName END, N'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_RelItemSuppliers ris{suffix} ON t2.fk_ItemID = ris{suffix}.fk_ItemID AND ISNULL(ris{suffix}.PrimarySupplier,0) = 1 LEFT JOIN tbl_Supplier sup{suffix} ON ris{suffix}.fk_SupplierNo = sup{suffix}.pk_SupplierNo",
                $"ISNULL(sup{suffix}.pk_SupplierNo, N''), ISNULL(CASE WHEN sup{suffix}.Company = 1 THEN sup{suffix}.LastCompanyName ELSE sup{suffix}.FirstName + N' ' + sup{suffix}.LastCompanyName END, N'N/A')",
                $"ISNULL(sup{suffix}.pk_SupplierNo, N'')"),

            GroupByType.Model => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_ModelID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(mod{suffix}.ModelCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(mod{suffix}.ModelCode)) + N' - ' + LTRIM(RTRIM(ISNULL(mod{suffix}.ModelNamePrimary, mod{suffix}.ModelCode))) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Model mod{suffix} ON {itemAlias}.fk_ModelID = mod{suffix}.pk_ModelID",
                $"CAST(ISNULL({itemAlias}.fk_ModelID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(mod{suffix}.ModelCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(mod{suffix}.ModelCode)) + N' - ' + LTRIM(RTRIM(ISNULL(mod{suffix}.ModelNamePrimary, mod{suffix}.ModelCode))) END",
                $"CAST(ISNULL({itemAlias}.fk_ModelID, 0) AS NVARCHAR(20))"),

            GroupByType.Colour => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_ColourID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(col{suffix}.ColourCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(col{suffix}.ColourCode)) + N' - ' + LTRIM(RTRIM(col{suffix}.ColourName)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Colour col{suffix} ON {itemAlias}.fk_ColourID = col{suffix}.pk_ColourID",
                $"CAST(ISNULL({itemAlias}.fk_ColourID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(col{suffix}.ColourCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(col{suffix}.ColourCode)) + N' - ' + LTRIM(RTRIM(col{suffix}.ColourName)) END",
                $"CAST(ISNULL({itemAlias}.fk_ColourID, 0) AS NVARCHAR(20))"),

            GroupByType.Size => new GroupingFragment(
                $"CAST(ISNULL({itemAlias}.fk_SizeID, 0) AS NVARCHAR(20)) AS {codeAlias}, ISNULL(sz{suffix}.SizeInvoiceDescr, N'N/A') AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Size sz{suffix} ON {itemAlias}.fk_SizeID = sz{suffix}.pk_SizeID",
                $"CAST(ISNULL({itemAlias}.fk_SizeID, 0) AS NVARCHAR(20)), ISNULL(sz{suffix}.SizeInvoiceDescr, N'N/A')",
                $"CAST(ISNULL({itemAlias}.fk_SizeID, 0) AS NVARCHAR(20))"),

            GroupByType.CustomerCategory1 => new GroupingFragment(
                $"CAST(ISNULL(cc1{suffix}.pk_CategoryID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(cc1{suffix}.CategoryCode, N'N/A') = ISNULL(cc1{suffix}.CategoryDescr, N'N/A') THEN ISNULL(cc1{suffix}.CategoryCode, N'N/A') ELSE LTRIM(RTRIM(ISNULL(cc1{suffix}.CategoryCode, N'N/A'))) + N' - ' + LTRIM(RTRIM(ISNULL(cc1{suffix}.CategoryDescr, N'N/A'))) END AS {nameAlias},",
                $"LEFT JOIN tbl_Customer cust1{suffix} ON t1.fk_CustomerCode = cust1{suffix}.pk_CustomerNo LEFT JOIN tbl_CustCategory cc1{suffix} ON cust1{suffix}.fk_Category1 = cc1{suffix}.pk_CategoryID",
                $"CAST(ISNULL(cc1{suffix}.pk_CategoryID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(cc1{suffix}.CategoryCode, N'N/A') = ISNULL(cc1{suffix}.CategoryDescr, N'N/A') THEN ISNULL(cc1{suffix}.CategoryCode, N'N/A') ELSE LTRIM(RTRIM(ISNULL(cc1{suffix}.CategoryCode, N'N/A'))) + N' - ' + LTRIM(RTRIM(ISNULL(cc1{suffix}.CategoryDescr, N'N/A'))) END",
                $"CAST(ISNULL(cc1{suffix}.pk_CategoryID, 0) AS NVARCHAR(20))"),

            GroupByType.CustomerCategory2 => new GroupingFragment(
                $"CAST(ISNULL(cc2{suffix}.pk_CategoryID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(cc2{suffix}.CategoryCode, N'N/A') = ISNULL(cc2{suffix}.CategoryDescr, N'N/A') THEN ISNULL(cc2{suffix}.CategoryCode, N'N/A') ELSE LTRIM(RTRIM(ISNULL(cc2{suffix}.CategoryCode, N'N/A'))) + N' - ' + LTRIM(RTRIM(ISNULL(cc2{suffix}.CategoryDescr, N'N/A'))) END AS {nameAlias},",
                $"LEFT JOIN tbl_Customer cust2{suffix} ON t1.fk_CustomerCode = cust2{suffix}.pk_CustomerNo LEFT JOIN tbl_CustCategory cc2{suffix} ON cust2{suffix}.fk_Category2 = cc2{suffix}.pk_CategoryID",
                $"CAST(ISNULL(cc2{suffix}.pk_CategoryID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(cc2{suffix}.CategoryCode, N'N/A') = ISNULL(cc2{suffix}.CategoryDescr, N'N/A') THEN ISNULL(cc2{suffix}.CategoryCode, N'N/A') ELSE LTRIM(RTRIM(ISNULL(cc2{suffix}.CategoryCode, N'N/A'))) + N' - ' + LTRIM(RTRIM(ISNULL(cc2{suffix}.CategoryDescr, N'N/A'))) END",
                $"CAST(ISNULL(cc2{suffix}.pk_CategoryID, 0) AS NVARCHAR(20))"),

            GroupByType.Item => new GroupingFragment(
                $"CAST(itm{suffix}.pk_ItemID AS NVARCHAR(20)) AS {codeAlias}, LTRIM(RTRIM(itm{suffix}.ItemCode)) + N' - ' + LTRIM(RTRIM(itm{suffix}.ItemNamePrimary)) AS {nameAlias},",
                $"LEFT JOIN tbl_Item itm{suffix} ON t2.fk_ItemID = itm{suffix}.pk_ItemID",
                $"CAST(itm{suffix}.pk_ItemID AS NVARCHAR(20)), LTRIM(RTRIM(itm{suffix}.ItemCode)) + N' - ' + LTRIM(RTRIM(itm{suffix}.ItemNamePrimary))",
                $"CAST(itm{suffix}.pk_ItemID AS NVARCHAR(20))"),

            GroupByType.GroupSize => new GroupingFragment(
                $"CAST(ISNULL(sg{suffix}.pk_SizeGroupID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(sg{suffix}.SizeGroupCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(sg{suffix}.SizeGroupCode)) + N' - ' + LTRIM(RTRIM(sg{suffix}.SizeGroupName)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Model mdl{suffix} ON {itemAlias}.fk_ModelID = mdl{suffix}.pk_ModelID LEFT JOIN tbl_SizeGroup sg{suffix} ON mdl{suffix}.fk_SizeGroupID = sg{suffix}.pk_SizeGroupID",
                $"CAST(ISNULL(sg{suffix}.pk_SizeGroupID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(sg{suffix}.SizeGroupCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(sg{suffix}.SizeGroupCode)) + N' - ' + LTRIM(RTRIM(sg{suffix}.SizeGroupName)) END",
                $"CAST(ISNULL(sg{suffix}.pk_SizeGroupID, 0) AS NVARCHAR(20))"),

            GroupByType.Fabric => new GroupingFragment(
                $"CAST(ISNULL(fab{suffix}.pk_FabricID, 0) AS NVARCHAR(20)) AS {codeAlias}, CASE WHEN ISNULL(fab{suffix}.FabricCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(fab{suffix}.FabricCode)) + N' - ' + LTRIM(RTRIM(fab{suffix}.FabricDescr)) END AS {nameAlias},",
                $"LEFT JOIN tbl_Item {itemAlias} ON t2.fk_ItemID = {itemAlias}.pk_ItemID LEFT JOIN tbl_Model mdl{suffix} ON {itemAlias}.fk_ModelID = mdl{suffix}.pk_ModelID LEFT JOIN tbl_Fabric fab{suffix} ON mdl{suffix}.fk_FabricID = fab{suffix}.pk_FabricID",
                $"CAST(ISNULL(fab{suffix}.pk_FabricID, 0) AS NVARCHAR(20)), CASE WHEN ISNULL(fab{suffix}.FabricCode, N'N/A') = N'N/A' THEN N'N/A' ELSE LTRIM(RTRIM(fab{suffix}.FabricCode)) + N' - ' + LTRIM(RTRIM(fab{suffix}.FabricDescr)) END",
                $"CAST(ISNULL(fab{suffix}.pk_FabricID, 0) AS NVARCHAR(20))"),

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

        string sqlExpr;
        if (col == "Sales" && filter.IncludeVat)
            sqlExpr = "(d.NetSales + d.VatSales - d.NetReturns - d.VatReturns)";
        else if (col == "AvgBasket" && filter.IncludeVat)
            sqlExpr = "CASE WHEN (d.InvoiceCount - d.CreditCount) > 0 THEN (d.NetSales + d.VatSales - d.NetReturns - d.VatReturns) * 1.0 / (d.InvoiceCount - d.CreditCount) ELSE 0 END";
        else if (!ColumnSqlMap.TryGetValue(col, out sqlExpr!))
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

    private string GetLYPeriodFieldShifted(BreakdownType breakdown)
    {
        return breakdown switch
        {
            BreakdownType.Daily => "CONVERT(VARCHAR(10), DATEADD(YEAR, 1, t1.DateTrans), 120)",
            BreakdownType.Weekly => "CONVERT(VARCHAR(4), DATEPART(YEAR, t1.DateTrans) + 1) + '-W' + RIGHT('00' + CONVERT(VARCHAR(2), DATEPART(WEEK, t1.DateTrans)), 2)",
            BreakdownType.Monthly => "CONVERT(VARCHAR(7), DATEADD(YEAR, 1, t1.DateTrans), 120)",
            _ => "CONVERT(VARCHAR(7), DATEADD(YEAR, 1, t1.DateTrans), 120)"
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
        ReportFilter filter,
        string dimJoin = "", string dimWhere = "")
    {
        var storeWhere = storeFilter.whereClause;
        var itemWhere = itemFilter.whereClause;
        var includeLastYear = filter.CompareLastYear;
        bool hasL2 = grouping.hasLevel2;
        bool hasStoreGroup = filter.GroupBy == GroupByType.Store || filter.SecondaryGroupBy == GroupByType.Store;

        var groupSelectClause = grouping.select;
        var groupJoinClause = grouping.join;
        var groupByClause = $", {grouping.groupByFields}";

        var cyJoin = "s.Period = r.Period AND s.GroupCode = r.GroupCode";
        if (hasL2) cyJoin += " AND s.Group2Code = r.Group2Code";

        var lyJoinBase = "ly.Period = cy.Period AND ly.GroupCode = cy.GroupCode";
        if (hasL2) lyJoinBase += " AND ly.Group2Code = cy.Group2Code";

        var g2CySelect = hasL2
            ? "COALESCE(s.Group2Code, r.Group2Code) AS Group2Code,\n                COALESCE(s.Group2Name, r.Group2Name) AS Group2Name,"
            : "NULL AS Group2Code, NULL AS Group2Name,";

        var storeAreaCySelect = hasStoreGroup
            ? "\n                COALESCE(s.StoreArea, r.StoreArea) AS StoreArea,"
            : "";
        var storeAreaFinalSelect = hasStoreGroup
            ? ",\n                cy.StoreArea"
            : "";

        var g2FinalSelect = $",\n                cy.Group2Code, cy.Group2Name{storeAreaFinalSelect}";

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
            {groupJoinClause}{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
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
            {creditJoin}{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
            GROUP BY {periodField}{groupByClause}";

        var cyCte = $@"
            SELECT 
                COALESCE(s.Period, r.Period) AS Period,
                COALESCE(s.GroupCode, r.GroupCode) AS GroupCode,
                COALESCE(s.GroupName, r.GroupName) AS GroupName,
                {g2CySelect}{storeAreaCySelect}
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

            var lyPeriodField = GetLYPeriodFieldShifted(filter.Breakdown);

            var lySalesCte = $@"
            SELECT 
                {lyPeriodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice
            {groupJoinClause}{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
            GROUP BY {lyPeriodField}{groupByClause}";

            var lyReturnsCte = $@"
            SELECT 
                {lyPeriodField} AS Period,
                {groupSelectClause}
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Quantity) AS QtyReturned,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit
            {creditJoin}{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
            GROUP BY {lyPeriodField}{groupByClause}";

            var lyStoreAreaSelect = hasStoreGroup
                ? "\n                COALESCE(lys.StoreArea, lyr.StoreArea) AS StoreArea,"
                : "";

            var lyMergeCte = $@"
            SELECT 
                COALESCE(lys.Period, lyr.Period) AS Period,
                COALESCE(lys.GroupCode, lyr.GroupCode) AS GroupCode,
                COALESCE(lys.GroupName, lyr.GroupName) AS GroupName,
                {g2LyCySelect}{lyStoreAreaSelect}
                ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(lyr.CreditCount, 0) AS CreditCount,
                ISNULL(lys.QtySold, 0) AS QtySold,
                ISNULL(lyr.QtyReturned, 0) AS QtyReturned,
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
                    ISNULL(ly.QtySold, 0) AS LYQtySold,
                    ISNULL(ly.QtyReturned, 0) AS LYQtyReturned,
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
        string periodField, string storeWhere, string itemWhere, ReportFilter filter,
        string dimJoin = "", string dimWhere = "")
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
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
            GROUP BY {periodField}";

        var returnsCte = $@"
            SELECT 
                {periodField} AS Period,
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Quantity) AS QtyReturned,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
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
            var lyPeriodField = GetLYPeriodFieldShifted(filter.Breakdown);
            lyCtes = $@",
                LYSales AS (
            SELECT {lyPeriodField} AS Period,
                COUNT(DISTINCT t1.pk_InvoiceID) AS InvoiceCount,
                SUM(t2.Quantity) AS QtySold,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetSales,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatSales
            FROM tbl_InvoiceHeader t1
            INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
            GROUP BY {lyPeriodField}
                ),
                LYReturns AS (
            SELECT {lyPeriodField} AS Period,
                COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                SUM(t2.Quantity) AS QtyReturned,
                SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
            FROM tbl_CreditHeader t1
            INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit{dimJoin}
            WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
            GROUP BY {lyPeriodField}
                ),
                LY AS (
            SELECT COALESCE(lys.Period, lyr.Period) AS Period,
                ISNULL(lys.InvoiceCount, 0) AS InvoiceCount,
                ISNULL(lyr.CreditCount, 0) AS CreditCount,
                ISNULL(lys.QtySold, 0) AS QtySold,
                ISNULL(lyr.QtyReturned, 0) AS QtyReturned,
                ISNULL(lys.NetSales, 0) AS NetSales,
                ISNULL(lyr.NetReturns, 0) AS NetReturns,
                ISNULL(lys.VatSales, 0) AS VatSales,
                ISNULL(lyr.VatReturns, 0) AS VatReturns
            FROM LYSales lys
            FULL OUTER JOIN LYReturns lyr ON lys.Period = lyr.Period
                )";
            lyJoin = @"
                LEFT JOIN LY ly ON ly.Period = cy.Period";
            lySelectColumns = @",
                    ISNULL(ly.InvoiceCount, 0) AS LYInvoiceCount,
                    ISNULL(ly.CreditCount, 0) AS LYCreditCount,
                    ISNULL(ly.QtySold, 0) AS LYQtySold,
                    ISNULL(ly.QtyReturned, 0) AS LYQtyReturned,
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
        ReportFilter filter,
        string dimJoin = "", string dimWhere = "")
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
                ISNULL(LYI.LYQtySold, 0) AS LYQtySold,
                ISNULL(LYC.LYQtyReturned, 0) AS LYQtyReturned,
                ISNULL(LYI.LYNetSales, 0) AS LYNetSales,
                ISNULL(LYC.LYNetReturns, 0) AS LYNetReturns,
                ISNULL(LYI.LYVatSales, 0) AS LYVatSales,
                ISNULL(LYC.LYVatReturns, 0) AS LYVatReturns";

            lySubQueries = $@"
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_InvoiceID) AS LYInvoiceCount,
                       SUM(t2.Quantity) AS LYQtySold,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS LYNetSales,
                       SUM(ISNULL(t2.VatAmount, 0)) AS LYVatSales
                FROM tbl_InvoiceHeader t1
                INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice{dimJoin}
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
            ) LYI ON 1=1
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_CreditID) AS LYCreditCount,
                       SUM(t2.Quantity) AS LYQtyReturned,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS LYNetReturns,
                       SUM(ISNULL(t2.VatAmount, 0)) AS LYVatReturns
                FROM tbl_CreditHeader t1
                INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit{dimJoin}
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN DATEADD(YEAR, -1, @DateFrom) AND DATEADD(YEAR, -1, @DateTo){storeWhere}{itemWhere}{dimWhere}
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
                INNER JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID = t2.fk_Invoice{dimJoin}
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
            ) SI
            LEFT JOIN (
                SELECT COUNT(DISTINCT t1.pk_CreditID) AS CreditCount,
                       SUM(t2.Quantity) AS QtyReturned,
                       SUM(t2.Amount - ISNULL(t2.Discount, 0) - ISNULL(t2.ExtraDiscount, 0)) AS NetReturns,
                       SUM(ISNULL(t2.VatAmount, 0)) AS VatReturns
                FROM tbl_CreditHeader t1
                INNER JOIN tbl_CreditDetails t2 ON t1.pk_CreditID = t2.fk_Credit{dimJoin}
                WHERE CONVERT(DATE, t1.DateTrans) BETWEEN @DateFrom AND @DateTo{storeWhere}{itemWhere}{dimWhere}
            ) CR ON 1=1{lySubQueries}";
    }

    #endregion

    #region Query Execution

    private async Task<int> GetTotalCountAsync(
        SqlConnection conn, string countSql, ReportFilter filter, List<SqlParameter> filterParams,
        List<SqlParameter>? dimParams = null)
    {
        using var cmd = new SqlCommand(countSql, conn);
        AddCommonParameters(cmd, filter);
        AddFilterParameters(cmd, filterParams);
        if (dimParams != null) AddFilterParameters(cmd, dimParams);
        
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<List<AverageBasketRow>> ExecuteMainQueryAsync(
        SqlConnection conn, 
        string sql, 
        ReportFilter filter, 
        bool hasLevel1,
        bool hasLevel2,
        List<SqlParameter> filterParams,
        List<SqlParameter>? dimParams = null)
    {
        var results = new List<AverageBasketRow>();
        
        using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, filter);
        AddFilterParameters(cmd, filterParams);
        if (dimParams != null) AddFilterParameters(cmd, dimParams);
        cmd.Parameters.AddWithValue("@Skip", filter.Skip);
        cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var hasLy = filter.CompareLastYear;
        bool hasStoreGroup = filter.GroupBy == GroupByType.Store || filter.SecondaryGroupBy == GroupByType.Store;

        int colIdx(string name) { try { return reader.GetOrdinal(name); } catch { return -1; } }
        
        while (await reader.ReadAsync())
        {
            var iPeriod = colIdx("Period");
            var iGroupCode = colIdx("GroupCode");
            var iGroupName = colIdx("GroupName");
            var iGroup2Code = colIdx("Group2Code");
            var iGroup2Name = colIdx("Group2Name");
            var iStoreArea = colIdx("StoreArea");
            var iInvoiceCount = colIdx("InvoiceCount");
            var iCreditCount = colIdx("CreditCount");
            var iQtySold = colIdx("QtySold");
            var iQtyReturned = colIdx("QtyReturned");
            var iNetSales = colIdx("NetSales");
            var iNetReturns = colIdx("NetReturns");
            var iVatSales = colIdx("VatSales");
            var iVatReturns = colIdx("VatReturns");

            var row = new AverageBasketRow
            {
                Period = reader.GetString(iPeriod),
                Level1 = reader.IsDBNull(iGroupCode) ? null : reader.GetString(iGroupCode),
                Level1Value = reader.IsDBNull(iGroupName) ? null : reader.GetString(iGroupName),
                Level2 = reader.IsDBNull(iGroup2Code) ? null : reader.GetString(iGroup2Code),
                Level2Value = reader.IsDBNull(iGroup2Name) ? null : reader.GetString(iGroup2Name),
                StoreArea = iStoreArea >= 0 && !reader.IsDBNull(iStoreArea) ? reader.GetDecimal(iStoreArea) : 0,
                CYInvoiceCount = reader.GetInt32(iInvoiceCount),
                CYCreditCount = reader.GetInt32(iCreditCount),
                CYQtySold = reader.IsDBNull(iQtySold) ? 0 : reader.GetDecimal(iQtySold),
                CYQtyReturned = reader.IsDBNull(iQtyReturned) ? 0 : reader.GetDecimal(iQtyReturned),
                CYNetSales = reader.IsDBNull(iNetSales) ? 0 : reader.GetDecimal(iNetSales),
                CYNetReturns = reader.IsDBNull(iNetReturns) ? 0 : reader.GetDecimal(iNetReturns),
                CYVatSales = reader.IsDBNull(iVatSales) ? 0 : reader.GetDecimal(iVatSales),
                CYVatReturns = reader.IsDBNull(iVatReturns) ? 0 : reader.GetDecimal(iVatReturns)
            };
            
            row.CYGrossSales = row.CYNetSales + row.CYVatSales;
            row.CYGrossReturns = row.CYNetReturns + row.CYVatReturns;
            
            if (hasLy)
            {
                var iLYInvoiceCount = colIdx("LYInvoiceCount");
                var iLYCreditCount = colIdx("LYCreditCount");
                var iLYQtySold = colIdx("LYQtySold");
                var iLYQtyReturned = colIdx("LYQtyReturned");
                var iLYNetSales = colIdx("LYNetSales");
                var iLYNetReturns = colIdx("LYNetReturns");
                var iLYVatSales = colIdx("LYVatSales");
                var iLYVatReturns = colIdx("LYVatReturns");

                row.LYInvoiceCount = iLYInvoiceCount >= 0 && !reader.IsDBNull(iLYInvoiceCount) ? reader.GetInt32(iLYInvoiceCount) : 0;
                row.LYCreditCount = iLYCreditCount >= 0 && !reader.IsDBNull(iLYCreditCount) ? reader.GetInt32(iLYCreditCount) : 0;
                row.LYQtySold = iLYQtySold >= 0 && !reader.IsDBNull(iLYQtySold) ? reader.GetDecimal(iLYQtySold) : 0;
                row.LYQtyReturned = iLYQtyReturned >= 0 && !reader.IsDBNull(iLYQtyReturned) ? reader.GetDecimal(iLYQtyReturned) : 0;
                row.LYNetSales = iLYNetSales >= 0 && !reader.IsDBNull(iLYNetSales) ? reader.GetDecimal(iLYNetSales) : 0;
                row.LYNetReturns = iLYNetReturns >= 0 && !reader.IsDBNull(iLYNetReturns) ? reader.GetDecimal(iLYNetReturns) : 0;
                row.LYVatSales = iLYVatSales >= 0 && !reader.IsDBNull(iLYVatSales) ? reader.GetDecimal(iLYVatSales) : 0;
                row.LYVatReturns = iLYVatReturns >= 0 && !reader.IsDBNull(iLYVatReturns) ? reader.GetDecimal(iLYVatReturns) : 0;
                row.LYTotalNet = row.LYNetSales - row.LYNetReturns;
                row.LYTotalGross = row.LYTotalNet + (row.LYVatSales - row.LYVatReturns);
            }
            
            results.Add(row);
        }
        
        return results;
    }

    private async Task<ReportGrandTotals> ExecuteGrandTotalsQueryAsync(SqlConnection conn, string sql, ReportFilter filter,
        List<SqlParameter>? dimParams = null)
    {
        var totals = new ReportGrandTotals();
        
        using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, filter);
        if (dimParams != null) AddFilterParameters(cmd, dimParams);
        
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
                totals.LYQtySold = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10);
                totals.LYQtyReturned = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11);
                totals.LYNetSales = reader.IsDBNull(12) ? 0 : reader.GetDecimal(12);
                totals.LYNetReturns = reader.IsDBNull(13) ? 0 : reader.GetDecimal(13);
                totals.LYVatSales = reader.IsDBNull(14) ? 0 : reader.GetDecimal(14);
                totals.LYVatReturns = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15);
            }
        }
        
        return totals;
    }

    private void AddCommonParameters(SqlCommand cmd, ReportFilter filter)
    {
        cmd.CommandTimeout = 120;
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
