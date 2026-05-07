using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class ParetoRepository : IParetoRepository
{
    private readonly string _connectionString;

    public ParetoRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ParetoResult> GetParetoDataAsync(ParetoFilter filter)
    {
        var (codeExpr, nameExpr, outerJoinSql, outerGroupBy) = GetDimensionSql(filter.Dimension);
        var storeWhere = BuildStoreWhere(filter.StoreCodes);
        var (dimWhere, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection);
        var customerWhere = BuildCustomerWhere(filter);
        var profitExpr = BuildProfitExpression(filter);
        var excludeWhere = BuildExcludeNegativeWhere(filter);
        var priceExpr = BuildPriceExpression(filter);

        var dateCondition = filter.TimezoneOffsetMinutes != 0
            ? "CONVERT(DATE, DATEADD(MINUTE, @tzOffsetMin, h.DateTrans)) BETWEEN @DateFrom AND @DateTo"
            : "CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo";

        var innerSelect = $@"
            SELECT h.fk_CustomerCode, h.fk_StoreCode, h.fk_UserCode,
                   d.fk_ItemID,
                   (d.Amount - d.Discount - d.ExtraDiscount) AS Subtotal,
                   d.Quantity,
                   d.ItemPriceExcl, d.ItemPriceIncl
            FROM tbl_InvoiceDetails d WITH (NOLOCK)
            INNER JOIN tbl_InvoiceHeader h ON d.fk_Invoice = h.pk_InvoiceID
            WHERE {dateCondition}

            UNION ALL

            SELECT h.fk_CustomerCode, h.fk_StoreCode, h.fk_UserCode,
                   d.fk_ItemID,
                   (d.Amount - d.Discount - d.ExtraDiscount) * (-1) AS Subtotal,
                   d.Quantity * (-1) AS Quantity,
                   d.ItemPriceExcl, d.ItemPriceIncl
            FROM tbl_CreditDetails d WITH (NOLOCK)
            INNER JOIN tbl_CreditHeader h ON d.fk_Credit = h.pk_CreditID
            WHERE {dateCondition}";

        var sql = $@"
            ;WITH RawTrans AS (
                {innerSelect}
            ),
            Enriched AS (
                SELECT dt.fk_CustomerCode, dt.fk_StoreCode, dt.fk_UserCode,
                       dt.fk_ItemID, dt.Subtotal, dt.Quantity,
                       dt.ItemPriceExcl, dt.ItemPriceIncl,
                       it.fk_BrandID, it.fk_CategoryID, it.fk_ColourID,
                       it.fk_DepartmentID, it.fk_ModelID, it.fk_SeasonID, it.fk_SizeID,
                       its.fk_SupplierNo,
                       cs.fk_Category1, cs.fk_Category2,
                       {profitExpr} AS Profit,
                       {priceExpr} AS Price
                FROM RawTrans dt
                INNER JOIN tbl_Item it ON dt.fk_ItemID = it.pk_ItemID
                LEFT JOIN tbl_Customer cs ON dt.fk_CustomerCode = cs.pk_CustomerNo
                LEFT JOIN tbl_RelItemSuppliers its ON dt.fk_ItemID = its.fk_ItemID AND ISNULL(its.PrimarySupplier,0) = 1
                WHERE 1=1 {dimWhere}{customerWhere}{excludeWhere}
            ),
            Grouped AS (
                SELECT {codeExpr} AS Code, {nameExpr} AS Name,
                       SUM(e.Quantity) AS TotalQty,
                       SUM(e.Subtotal) AS TotalSubtotal,
                       SUM(e.Profit) AS TotalProfit
                FROM Enriched e
                {outerJoinSql}
                {storeWhere}
                GROUP BY {outerGroupBy}
            )
            SELECT Code, Name, TotalQty, TotalSubtotal, TotalProfit
            FROM Grouped
            ORDER BY {GetOrderByExpression(filter.Metric)} DESC";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var rawData = new List<(string code, string name, decimal qty, decimal subtotal, decimal profit)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom);
            cmd.Parameters.AddWithValue("@DateTo", filter.DateTo);
            if (filter.TimezoneOffsetMinutes != 0)
                cmd.Parameters.AddWithValue("@tzOffsetMin", filter.TimezoneOffsetMinutes);

            if (filter.Dimension == ParetoDimension.ByPrice)
                cmd.Parameters.AddWithValue("@PriceInterval", filter.PriceInterval > 0 ? filter.PriceInterval : 10m);

            if (filter.Metric == ParetoMetric.Profit || filter.ExcludeNegativeAmounts)
            {
                cmd.Parameters.AddWithValue("@ProfitBasis", (int)ResolveProfitBasis(filter));
                cmd.Parameters.AddWithValue("@IncludeVAT", filter.IncludeVat ? 1 : 0);
            }

            if (filter.StoreCodes != null)
                for (int i = 0; i < filter.StoreCodes.Count; i++)
                    cmd.Parameters.AddWithValue($"@SC{i}", filter.StoreCodes[i]);

            foreach (var p in dimParams)
                cmd.Parameters.Add(p);

            AddCustomerParams(cmd, filter);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rawData.Add((
                    reader.IsDBNull(0) ? "" : reader.GetString(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                    reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                    reader.IsDBNull(4) ? 0 : reader.GetDecimal(4)
                ));
            }
        }

        return BuildResult(rawData, filter);
    }

    private static ParetoResult BuildResult(
        List<(string code, string name, decimal qty, decimal subtotal, decimal profit)> rawData,
        ParetoFilter filter)
    {
        var result = new ParetoResult
        {
            TotalQuantity = rawData.Sum(r => r.qty),
            TotalSubtotal = rawData.Sum(r => r.subtotal),
            TotalProfit = rawData.Sum(r => r.profit)
        };

        decimal grandTotal = filter.Metric switch
        {
            ParetoMetric.Quantity => result.TotalQuantity,
            ParetoMetric.Profit => result.TotalProfit,
            _ => result.TotalSubtotal
        };
        result.GrandTotal = grandTotal;

        decimal cumulative = 0;
        int rank = 0;
        bool lastFound = false;

        foreach (var (code, name, qty, subtotal, profit) in rawData)
        {
            rank++;
            decimal metricValue = filter.Metric switch
            {
                ParetoMetric.Quantity => qty,
                ParetoMetric.Profit => profit,
                _ => subtotal
            };

            cumulative += metricValue;
            var pct = grandTotal != 0 ? (metricValue / grandTotal * 100) : 0;
            var cumPct = grandTotal != 0 ? (cumulative / grandTotal * 100) : 0;

            string classification;
            if (cumPct <= filter.ClassAThreshold)
            {
                classification = "A";
                result.ClassACount++;
                result.ClassAValue += metricValue;
            }
            else if (cumPct <= filter.ClassBThreshold)
            {
                classification = "B";
                result.ClassBCount++;
                result.ClassBValue += metricValue;
            }
            else
            {
                classification = "C";
                result.ClassCCount++;
                result.ClassCValue += metricValue;
            }

            var runningPct = grandTotal != 0 ? (cumulative / grandTotal * 100) : 0;
            bool isDisplay;
            if (runningPct <= filter.ClassAThreshold - 0.01m)
            {
                isDisplay = true;
            }
            else if (!lastFound)
            {
                isDisplay = true;
                lastFound = true;
            }
            else
            {
                isDisplay = false;
            }

            result.Rows.Add(new ParetoRow
            {
                Rank = rank,
                Code = code,
                Name = name,
                Value = metricValue,
                Quantity = qty,
                Subtotal = subtotal,
                Profit = profit,
                CumulativeValue = cumulative,
                Percentage = Math.Round(pct, 2),
                CumulativePercentage = Math.Round(cumPct, 2),
                Classification = classification,
                IsDisplay = isDisplay
            });
        }

        return result;
    }

    private static string GetOrderByExpression(ParetoMetric metric) => metric switch
    {
        ParetoMetric.Quantity => "TotalQty",
        ParetoMetric.Profit => "TotalProfit",
        _ => "TotalSubtotal"
    };

    private static (string codeExpr, string nameExpr, string joinSql, string groupBy) GetDimensionSql(ParetoDimension dim) => dim switch
    {
        ParetoDimension.Item => (
            "it.ItemCode",
            "ISNULL(it.ItemNamePrimary, it.ItemCode)",
            "INNER JOIN tbl_Item it ON e.fk_ItemID = it.pk_ItemID",
            "it.ItemCode, it.ItemNamePrimary"),

        ParetoDimension.Customer => (
            "ISNULL(e.fk_CustomerCode, '')",
            "ISNULL(CASE WHEN c.Company=1 THEN c.LastCompanyName ELSE c.FirstName+' '+c.LastCompanyName END, 'Unknown')",
            "LEFT JOIN tbl_Customer c ON e.fk_CustomerCode = c.pk_CustomerNo",
            "e.fk_CustomerCode, c.Company, c.FirstName, c.LastCompanyName"),

        ParetoDimension.CustomerCategory1 => (
            "ISNULL(cc.CategoryCode, 'N/A')",
            "ISNULL(cc.CategoryDescr, 'UNDEFINED')",
            "LEFT JOIN tbl_CustCategory cc ON e.fk_Category1 = cc.pk_CategoryID AND ISNULL(cc.Filter,1) = 1",
            "cc.CategoryCode, cc.CategoryDescr"),

        ParetoDimension.CustomerCategory2 => (
            "ISNULL(cc.CategoryCode, 'N/A')",
            "ISNULL(cc.CategoryDescr, 'UNDEFINED')",
            "LEFT JOIN tbl_CustCategory cc ON e.fk_Category2 = cc.pk_CategoryID AND ISNULL(cc.Filter,1) = 2",
            "cc.CategoryCode, cc.CategoryDescr"),

        ParetoDimension.Category => (
            "ISNULL(cat.CategoryCode, 'N/A')",
            "ISNULL(LTRIM(RTRIM(cat.CategoryCode))+' - '+LTRIM(RTRIM(cat.CategoryDescr)), 'Uncategorized')",
            "LEFT JOIN tbl_ItemCategory cat ON e.fk_CategoryID = cat.pk_CategoryID",
            "cat.CategoryCode, cat.CategoryDescr"),

        ParetoDimension.Department => (
            "ISNULL(dep.DepartmentCode, 'N/A')",
            "ISNULL(dep.DepartmentDescr, 'UNDEFINED')",
            "LEFT JOIN tbl_ItemDepartment dep ON e.fk_DepartmentID = dep.pk_DepartmentID",
            "dep.DepartmentCode, dep.DepartmentDescr"),

        ParetoDimension.Brand => (
            "ISNULL(br.BrandCode, 'N/A')",
            "ISNULL(LTRIM(RTRIM(br.BrandCode))+' - '+LTRIM(RTRIM(br.BrandDesc)), 'No Brand')",
            "LEFT JOIN tbl_Brands br ON e.fk_BrandID = br.pk_BrandID",
            "br.BrandCode, br.BrandDesc"),

        ParetoDimension.Season => (
            "ISNULL(sea.SeasonCode, 'N/A')",
            "ISNULL(sea.SeasonDesc, 'UNDEFINED')",
            "LEFT JOIN tbl_Season sea ON e.fk_SeasonID = sea.pk_SeasonID",
            "sea.SeasonCode, sea.SeasonDesc"),

        ParetoDimension.Supplier => (
            "ISNULL(e.fk_SupplierNo, 'N/A')",
            "ISNULL(CASE WHEN sup.Company=1 THEN sup.LastCompanyName ELSE sup.FirstName+' '+sup.LastCompanyName END, 'Unknown')",
            "LEFT JOIN tbl_Supplier sup ON e.fk_SupplierNo = sup.pk_SupplierNo",
            "e.fk_SupplierNo, sup.Company, sup.FirstName, sup.LastCompanyName"),

        ParetoDimension.Model => (
            "ISNULL(md.ModelCode, 'N/A')",
            "ISNULL(md.ModelNamePrimary, 'UNDEFINED')",
            "LEFT JOIN tbl_Model md ON e.fk_ModelID = md.pk_ModelID",
            "md.ModelCode, md.ModelNamePrimary"),

        ParetoDimension.Colour => (
            "ISNULL(col.ColourCode, 'N/A')",
            "ISNULL(col.ColourName, 'UNDEFINED')",
            "LEFT JOIN tbl_Colour col ON e.fk_ColourID = col.pk_ColourID",
            "col.ColourCode, col.ColourName"),

        ParetoDimension.Size => (
            "ISNULL(sz.SizeCode, 'N/A')",
            "ISNULL(sz.SizeInvoiceDescr, ISNULL(sz.SizeName, 'UNDEFINED'))",
            "LEFT JOIN tbl_Size sz ON e.fk_SizeID = sz.pk_SizeID",
            "sz.SizeCode, sz.SizeInvoiceDescr, sz.SizeName"),

        ParetoDimension.GroupSize => (
            "ISNULL(sg.SizeGroupCode, 'N/A')",
            "ISNULL(sg.SizeGroupName, 'UNDEFINED')",
            "LEFT JOIN tbl_Model mdg ON e.fk_ModelID = mdg.pk_ModelID LEFT JOIN tbl_SizeGroup sg ON mdg.fk_SizeGroupID = sg.pk_SizeGroupID",
            "sg.SizeGroupCode, sg.SizeGroupName"),

        ParetoDimension.Fabric => (
            "ISNULL(fab.FabricCode, 'N/A')",
            "ISNULL(fab.FabricDescr, 'UNDEFINED')",
            "LEFT JOIN tbl_Model mdf ON e.fk_ModelID = mdf.pk_ModelID LEFT JOIN tbl_Fabric fab ON mdf.fk_FabricID = fab.pk_FabricID",
            "fab.FabricCode, fab.FabricDescr"),

        ParetoDimension.Store => (
            "ISNULL(st.pk_StoreCode, 'N/A')",
            "ISNULL(st.StoreName, 'UNDEFINED')",
            "LEFT JOIN tbl_Store st ON e.fk_StoreCode = st.pk_StoreCode",
            "st.pk_StoreCode, st.StoreName"),

        ParetoDimension.User => (
            "ISNULL(e.fk_UserCode, 'N/A')",
            "ISNULL(e.fk_UserCode, 'UNDEFINED')",
            "",
            "e.fk_UserCode"),

        ParetoDimension.ByPrice => (
            "'[' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval AS NVARCHAR(20)))) + ' - ' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval + @PriceInterval - 0.01 AS NVARCHAR(20)))) + ']'",
            "'[' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval AS NVARCHAR(20)))) + ' - ' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval + @PriceInterval - 0.01 AS NVARCHAR(20)))) + ']'",
            "",
            "'[' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval AS NVARCHAR(20)))) + ' - ' + LTRIM(RTRIM(CAST(CAST(e.Price / @PriceInterval AS INT) * @PriceInterval + @PriceInterval - 0.01 AS NVARCHAR(20)))) + ']'"),

        _ => ("it.ItemCode", "it.ItemCode", "INNER JOIN tbl_Item it ON e.fk_ItemID = it.pk_ItemID", "it.ItemCode")
    };

    private static string BuildProfitExpression(ParetoFilter filter)
    {
        var basis = ResolveProfitBasis(filter);
        return basis switch
        {
            ParetoProfitBasis.LatestCost => "(dt.Subtotal - ISNULL(it.Cost, 0) * dt.Quantity)",
            ParetoProfitBasis.AverageCost => "(dt.Subtotal - ISNULL(it.AverageCost, 0) * dt.Quantity)",
            ParetoProfitBasis.WeightedAverageCost => "(dt.Subtotal - ISNULL(it.WeightedAverageCost, 0) * dt.Quantity)",
            ParetoProfitBasis.DefaultPrice => BuildPriceProfitExpr(filter.DefaultPriceIndex, filter.IncludeVat),
            _ => BuildPriceProfitExpr((int)basis, filter.IncludeVat)
        };
    }

    private static ParetoProfitBasis ResolveProfitBasis(ParetoFilter filter)
    {
        if (filter.ProfitBasis == ParetoProfitBasis.DefaultPrice)
        {
            if (filter.DefaultPriceIndex >= 1 && filter.DefaultPriceIndex <= 10)
                return (ParetoProfitBasis)filter.DefaultPriceIndex;
            return ParetoProfitBasis.LatestCost;
        }
        return filter.ProfitBasis;
    }

    private static string BuildPriceProfitExpr(int priceIndex, bool includeVat)
    {
        if (priceIndex < 1 || priceIndex > 10) priceIndex = 1;
        var col = includeVat ? $"it.Price{priceIndex}Incl" : $"it.Price{priceIndex}Excl";
        return $"(dt.Subtotal - ISNULL({col}, 0) * dt.Quantity)";
    }

    private static string BuildPriceExpression(ParetoFilter filter)
    {
        var idx = filter.PriceOnIndex;
        if (idx >= 1 && idx <= 10)
        {
            var col = filter.PriceOnIncludesVat ? $"it.Price{idx}Incl" : $"it.Price{idx}Excl";
            return $"ISNULL({col}, 0)";
        }
        return filter.PriceOnIncludesVat ? "ISNULL(dt.ItemPriceIncl, 0)" : "ISNULL(dt.ItemPriceExcl, 0)";
    }

    private static string BuildStoreWhere(List<string>? storeCodes)
    {
        if (storeCodes == null || storeCodes.Count == 0) return "";
        var list = string.Join(",", storeCodes.Select((_, i) => $"@SC{i}"));
        return $"WHERE e.fk_StoreCode IN ({list})";
    }

    private static string BuildExcludeNegativeWhere(ParetoFilter filter)
    {
        if (!filter.ExcludeNegativeAmounts) return "";
        return filter.Metric switch
        {
            ParetoMetric.Quantity => " AND dt.Quantity > 0",
            ParetoMetric.Profit => $" AND ({BuildProfitExpression(filter)}) > 0",
            _ => " AND dt.Subtotal > 0"
        };
    }

    private static string BuildCustomerWhere(ParetoFilter filter)
    {
        var sb = new System.Text.StringBuilder();

        if (filter.CustomerCodes is { Count: > 0 })
        {
            var list = string.Join(",", filter.CustomerCodes.Select((_, i) => $"@CustCode{i}"));
            sb.Append($" AND dt.fk_CustomerCode IN ({list})");
        }

        if (filter.CustomerCategory1Ids is { Count: > 0 })
        {
            var list = string.Join(",", filter.CustomerCategory1Ids.Select((_, i) => $"@CC1_{i}"));
            sb.Append($" AND cs.fk_Category1 IN ({list})");
        }

        if (filter.CustomerCategory2Ids is { Count: > 0 })
        {
            var list = string.Join(",", filter.CustomerCategory2Ids.Select((_, i) => $"@CC2_{i}"));
            sb.Append($" AND cs.fk_Category2 IN ({list})");
        }

        return sb.ToString();
    }

    private static void AddCustomerParams(SqlCommand cmd, ParetoFilter filter)
    {
        if (filter.CustomerCodes != null)
            for (int i = 0; i < filter.CustomerCodes.Count; i++)
                cmd.Parameters.AddWithValue($"@CustCode{i}", filter.CustomerCodes[i]);

        if (filter.CustomerCategory1Ids != null)
            for (int i = 0; i < filter.CustomerCategory1Ids.Count; i++)
                cmd.Parameters.AddWithValue($"@CC1_{i}", filter.CustomerCategory1Ids[i]);

        if (filter.CustomerCategory2Ids != null)
            for (int i = 0; i < filter.CustomerCategory2Ids.Count; i++)
                cmd.Parameters.AddWithValue($"@CC2_{i}", filter.CustomerCategory2Ids[i]);
    }
}
