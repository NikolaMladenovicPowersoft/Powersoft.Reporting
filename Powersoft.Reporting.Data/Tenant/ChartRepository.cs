using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class ChartRepository : IChartRepository
{
    private readonly string _connectionString;

    public ChartRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<ChartDataPoint>> GetSalesBreakdownAsync(ChartFilter filter)
    {
        if (filter.Mode == ChartMode.SalesVsReturns
            || filter.Mode == ChartMode.PurchasesVsReturns
            || filter.Mode == ChartMode.SalesVsPurchases)
        {
            return await GetVsBreakdownAsync(filter);
        }

        return await GetSingleSeriesBreakdownAsync(filter);
    }

    private async Task<List<ChartDataPoint>> GetSingleSeriesBreakdownAsync(ChartFilter filter)
    {
        bool isPurchase = filter.Mode == ChartMode.Purchases;
        var (dimSelect, dimJoin, dimGroup) = GetDimensionSql(filter.Dimension, isPurchase);
        var rawValueExpr = GetRawValueExpression(filter);
        var storeWhere = BuildStoreWhere(filter.StoreCodes);
        var (dimWhere, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection);

        var sb = new StringBuilder();
        sb.AppendLine("SELECT TOP(@TopN) Label, SUM(Val) AS Val");
        sb.AppendLine("FROM (");
        AppendTransactionLeg(sb, filter, isPurchase ? "PI" : "SI", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere);
        sb.AppendLine("  UNION ALL");
        AppendReturnLeg(sb, filter, isPurchase ? "PR" : "SR", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere);
        sb.AppendLine(") sub");
        sb.AppendLine("GROUP BY Label");
        sb.AppendLine("ORDER BY Val DESC");

        string mainSql = sb.ToString();

        string? totalSql = null;
        if (filter.ShowOthers)
        {
            var tsb = new StringBuilder();
            tsb.AppendLine("SELECT SUM(Val) AS Val");
            tsb.AppendLine("FROM (");
            AppendTransactionLeg(tsb, filter, isPurchase ? "PI" : "SI", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere);
            tsb.AppendLine("  UNION ALL");
            AppendReturnLeg(tsb, filter, isPurchase ? "PR" : "SR", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere);
            tsb.AppendLine(") sub");
            totalSql = tsb.ToString();
        }

        string? compareSql = null;
        if (filter.CompareLastYear)
        {
            var csb = new StringBuilder();
            csb.AppendLine("SELECT TOP(@TopN) Label, SUM(Val) AS Val");
            csb.AppendLine("FROM (");
            AppendTransactionLeg(csb, filter, isPurchase ? "PI" : "SI", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere, useLyDates: true);
            csb.AppendLine("  UNION ALL");
            AppendReturnLeg(csb, filter, isPurchase ? "PR" : "SR", dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere, useLyDates: true);
            csb.AppendLine(") sub");
            csb.AppendLine("GROUP BY Label");
            csb.AppendLine("ORDER BY Val DESC");
            compareSql = csb.ToString();
        }

        return await ExecuteChartQuery(mainSql, totalSql, compareSql, filter);
    }

    private async Task<List<ChartDataPoint>> GetVsBreakdownAsync(ChartFilter filter)
    {
        bool isSalesVsPurch = filter.Mode == ChartMode.SalesVsPurchases;
        bool isPurchVsRet = filter.Mode == ChartMode.PurchasesVsReturns;

        string legType1, legType2;
        if (isSalesVsPurch)
        {
            legType1 = "SI";
            legType2 = "PI";
        }
        else if (isPurchVsRet)
        {
            legType1 = "PI";
            legType2 = "PR";
        }
        else
        {
            legType1 = "SI";
            legType2 = "SR";
        }

        bool isPurchase1 = legType1 is "PI" or "PR";
        bool isPurchase2 = legType2 is "PI" or "PR";

        var (dimSelect, dimJoin1, _) = GetDimensionSql(filter.Dimension, isPurchase1);
        var (_, dimJoin2, _) = GetDimensionSql(filter.Dimension, isPurchase2);
        var rawValueExpr = GetRawValueExpression(filter);
        var storeWhere = BuildStoreWhere(filter.StoreCodes);
        var (dimWhere, _) = DimensionFilterBuilder.Build(filter.ItemsSelection);

        var sql1 = new StringBuilder();
        sql1.AppendLine("SELECT TOP(@TopN) Label, SUM(Val) AS Val");
        sql1.AppendLine("FROM (");
        AppendRawLeg(sql1, filter, legType1, dimSelect, rawValueExpr, dimJoin1, storeWhere, dimWhere);
        sql1.AppendLine(") sub");
        sql1.AppendLine("GROUP BY Label");
        sql1.AppendLine("ORDER BY Val DESC");

        var sql2 = new StringBuilder();
        sql2.AppendLine("SELECT TOP(@TopN) Label, SUM(Val) AS Val");
        sql2.AppendLine("FROM (");
        AppendRawLeg(sql2, filter, legType2, dimSelect, rawValueExpr, dimJoin2, storeWhere, dimWhere);
        sql2.AppendLine(") sub");
        sql2.AppendLine("GROUP BY Label");
        sql2.AppendLine("ORDER BY Val DESC");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var series1 = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var series2 = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var allLabels = new List<string>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql1.ToString();
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var label = reader.IsDBNull(0) ? "N/A" : reader.GetString(0);
                var val = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                series1[label] = val;
                if (!allLabels.Contains(label)) allLabels.Add(label);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql2.ToString();
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var label = reader.IsDBNull(0) ? "N/A" : reader.GetString(0);
                var val = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                series2[label] = val;
                if (!allLabels.Contains(label)) allLabels.Add(label);
            }
        }

        var results = new List<ChartDataPoint>();
        foreach (var label in allLabels.Take(filter.TopN))
        {
            var v1 = series1.GetValueOrDefault(label, 0);
            var v2 = series2.GetValueOrDefault(label, 0);
            results.Add(new ChartDataPoint
            {
                Label = label,
                Value = v1,
                Value2 = v2,
                DiffValue = v1 - v2
            });
        }

        if (filter.ShowOthers)
        {
            // "Others" must aggregate the FULL tail of each series (everything not displayed),
            // not just the leftover among the two TOP(N) result sets. Compute each series' grand
            // total across all labels and subtract what is shown.
            var topLabels = allLabels.Take(filter.TopN).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var grand1 = await ExecuteSeriesGrandTotal(conn, filter, legType1, dimSelect, rawValueExpr, dimJoin1, storeWhere, dimWhere);
            var grand2 = await ExecuteSeriesGrandTotal(conn, filter, legType2, dimSelect, rawValueExpr, dimJoin2, storeWhere, dimWhere);
            var shown1 = series1.Where(kv => topLabels.Contains(kv.Key)).Sum(kv => kv.Value);
            var shown2 = series2.Where(kv => topLabels.Contains(kv.Key)).Sum(kv => kv.Value);
            var o1 = grand1 - shown1;
            var o2 = grand2 - shown2;
            if (o1 != 0 || o2 != 0)
            {
                results.Add(new ChartDataPoint
                {
                    Label = "Others",
                    Value = o1,
                    Value2 = o2,
                    DiffValue = o1 - o2
                });
            }
        }

        return results;
    }

    private async Task<decimal> ExecuteSeriesGrandTotal(
        SqlConnection conn, ChartFilter filter, string legType,
        string dimSelect, string rawValueExpr, string dimJoin, string storeWhere, string dimWhere)
    {
        var gsb = new StringBuilder();
        gsb.AppendLine("SELECT ISNULL(SUM(Val), 0) FROM (");
        AppendRawLeg(gsb, filter, legType, dimSelect, rawValueExpr, dimJoin, storeWhere, dimWhere);
        gsb.AppendLine(") sub");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = gsb.ToString();
        cmd.CommandTimeout = 30;
        AddParameters(cmd, filter);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
    }

    private void AppendTransactionLeg(StringBuilder sb, ChartFilter filter, string legType,
        string dimSelect, string valueExpr, string dimJoin, string storeWhere, string dimWhere,
        bool useLyDates = false)
    {
        AppendRawLeg(sb, filter, legType, dimSelect, valueExpr, dimJoin, storeWhere, dimWhere, useLyDates);
    }

    private void AppendReturnLeg(StringBuilder sb, ChartFilter filter, string legType,
        string dimSelect, string valueExpr, string dimJoin, string storeWhere, string dimWhere,
        bool useLyDates = false)
    {
        var negValueExpr = $"(-1) * ({valueExpr})";
        AppendRawLeg(sb, filter, legType, dimSelect, negValueExpr, dimJoin, storeWhere, dimWhere, useLyDates);
    }

    private static void AppendRawLeg(StringBuilder sb, ChartFilter filter, string legType,
        string dimSelect, string valueExpr, string dimJoin, string storeWhere, string dimWhere,
        bool useLyDates = false)
    {
        var (detailTable, headerTable, detailFk, headerPk, entityTable, entityPk, entityFk) = legType switch
        {
            "SI" => ("tbl_InvoiceDetails", "tbl_InvoiceHeader", "d.fk_Invoice", "h.pk_InvoiceID",
                     "tbl_Customer", "e.pk_CustomerNo", "h.fk_CustomerCode"),
            "SR" => ("tbl_CreditDetails", "tbl_CreditHeader", "d.fk_Credit", "h.pk_CreditID",
                     "tbl_Customer", "e.pk_CustomerNo", "h.fk_CustomerCode"),
            "PI" => ("tbl_PurchInvoiceDetails", "tbl_PurchInvoiceHeader", "d.fk_PurchInvoiceID", "h.pk_PurchInvoiceID",
                     "tbl_Supplier", "e.pk_SupplierNo", "h.fk_SupplierCode"),
            "PR" => ("tbl_PurchReturnDetails", "tbl_PurchReturnHeader", "d.fk_PurchReturnID", "h.pk_PurchReturnID",
                     "tbl_Supplier", "e.pk_SupplierNo", "h.fk_SupplierCode"),
            _ => throw new ArgumentException($"Unknown leg type: {legType}")
        };

        string dateFrom = useLyDates ? "@LYDateFrom" : "@DateFrom";
        string dateTo = useLyDates ? "@LYDateTo" : "@DateTo";

        sb.AppendLine($"    SELECT {dimSelect} AS Label, {valueExpr} AS Val");
        sb.AppendLine($"    FROM {detailTable} d");
        sb.AppendLine($"    INNER JOIN tbl_Item t2 ON d.fk_ItemID = t2.pk_ItemID");
        sb.AppendLine($"    INNER JOIN {headerTable} h ON {detailFk} = {headerPk}");

        if (!string.IsNullOrEmpty(dimJoin))
        {
            sb.AppendLine($"    {dimJoin}");
        }

        sb.AppendLine($"    WHERE h.DateTrans >= {dateFrom} AND h.DateTrans < DATEADD(DAY, 1, {dateTo})");
        sb.Append($"      {storeWhere.Replace("t3.", "h.")}");
        sb.AppendLine(dimWhere.Replace("t1.", "d."));
    }

    private async Task<List<ChartDataPoint>> ExecuteChartQuery(
        string mainSql, string? totalSql, string? compareSql, ChartFilter filter)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var results = new List<ChartDataPoint>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = mainSql;
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ChartDataPoint
                {
                    Label = reader.IsDBNull(0) ? "N/A" : reader.GetString(0),
                    Value = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1)
                });
            }
        }

        if (filter.ShowOthers && totalSql != null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = totalSql;
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var grandTotal = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                var topNSum = results.Sum(r => r.Value);
                var othersVal = grandTotal - topNSum;
                if (othersVal > 0)
                    results.Add(new ChartDataPoint { Label = "Others", Value = othersVal });
            }
        }

        if (filter.CompareLastYear && compareSql != null)
        {
            var lyData = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = compareSql;
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var lbl = reader.IsDBNull(0) ? "N/A" : reader.GetString(0);
                var val = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                lyData[lbl] = val;
            }

            foreach (var pt in results)
            {
                pt.CompareValue = lyData.GetValueOrDefault(pt.Label, 0);
                pt.DiffValue = pt.Value - (pt.CompareValue ?? 0);
            }
        }

        return results;
    }

    private static (string select, string join, string group) GetDimensionSql(ChartDimension dim, bool isPurchase = false)
    {
        var entityTable = isPurchase ? "tbl_Supplier" : "tbl_Customer";
        var entityPk = isPurchase ? "e.pk_SupplierNo" : "e.pk_CustomerNo";
        var entityFk = isPurchase ? "h.fk_SupplierCode" : "h.fk_CustomerCode";

        return dim switch
        {
            ChartDimension.Category => (
                "ISNULL(LTRIM(RTRIM(cat.CategoryCode))+' - '+LTRIM(RTRIM(cat.CategoryDescr)), 'Uncategorized')",
                "LEFT JOIN tbl_ItemCategory cat ON t2.fk_CategoryID = cat.pk_CategoryID",
                "cat.CategoryCode, cat.CategoryDescr"),
            ChartDimension.Store => (
                "ISNULL(LTRIM(RTRIM(st.pk_StoreCode))+' - '+LTRIM(RTRIM(st.StoreName)), 'N/A')",
                "LEFT JOIN tbl_Store st ON h.fk_StoreCode = st.pk_StoreCode",
                "st.pk_StoreCode, st.StoreName"),
            ChartDimension.Brand => (
                "ISNULL(LTRIM(RTRIM(br.BrandCode))+' - '+LTRIM(RTRIM(br.BrandDesc)), 'No Brand')",
                "LEFT JOIN tbl_Brands br ON t2.fk_BrandID = br.pk_BrandID",
                "br.BrandCode, br.BrandDesc"),
            ChartDimension.Customer => (
                $"ISNULL(CASE WHEN e.Company=1 THEN e.LastCompanyName ELSE e.FirstName+' '+e.LastCompanyName END, 'Unknown')",
                $"LEFT JOIN {entityTable} e ON {entityFk} = {entityPk}",
                "e.Company, e.FirstName, e.LastCompanyName"),
            ChartDimension.Item => (
                "ISNULL(t2.ItemCode+' - '+t2.ItemNamePrimary, t2.ItemCode)",
                "",
                "t2.ItemCode, t2.ItemNamePrimary"),
            ChartDimension.Supplier => (
                "ISNULL(CASE WHEN sup.Company=1 THEN sup.LastCompanyName ELSE sup.FirstName+' '+sup.LastCompanyName END, 'Unknown')",
                "LEFT JOIN tbl_RelItemSuppliers ris ON t2.pk_ItemID = ris.fk_ItemID AND ISNULL(ris.PrimarySupplier,0)=1 LEFT JOIN tbl_Supplier sup ON ris.fk_SupplierNo = sup.pk_SupplierNo",
                "sup.Company, sup.FirstName, sup.LastCompanyName"),
            ChartDimension.Department => (
                "ISNULL(LTRIM(RTRIM(dep.DepartmentCode))+' - '+LTRIM(RTRIM(dep.DepartmentDescr)), 'No Department')",
                "LEFT JOIN tbl_ItemDepartment dep ON t2.fk_DepartmentID = dep.pk_DepartmentID",
                "dep.DepartmentCode, dep.DepartmentDescr"),
            ChartDimension.Season => (
                "ISNULL(LTRIM(RTRIM(sea.SeasonCode))+' - '+LTRIM(RTRIM(sea.SeasonDescr)), 'No Season')",
                "LEFT JOIN tbl_Season sea ON t2.fk_SeasonID = sea.pk_SeasonID",
                "sea.SeasonCode, sea.SeasonDescr"),
            ChartDimension.Agent => (
                "ISNULL(LTRIM(RTRIM(ag.AgentCode))+' - '+LTRIM(RTRIM(ag.AgentName)), 'No Agent')",
                "LEFT JOIN tbl_Agent ag ON h.fk_AgentID = ag.pk_AgentID",
                "ag.AgentCode, ag.AgentName"),
            ChartDimension.User => (
                "ISNULL(h.fk_UserCode, 'Unknown')",
                "",
                "h.fk_UserCode"),
            ChartDimension.CSAgent => (
                $"ISNULL(LTRIM(RTRIM(cag.AgentCode))+' - '+LTRIM(RTRIM(cag.AgentName)), 'No Agent')",
                $"LEFT JOIN {entityTable} e ON {entityFk} = {entityPk} LEFT JOIN tbl_Agent cag ON e.fk_AgentID = cag.pk_AgentID",
                "cag.AgentCode, cag.AgentName"),
            ChartDimension.Model => (
                "ISNULL(LTRIM(RTRIM(mdl.ModelCode))+' - '+LTRIM(RTRIM(mdl.ModelNamePrimary)), 'No Model')",
                "LEFT JOIN tbl_Model mdl ON t2.fk_ModelID = mdl.pk_ModelID",
                "mdl.ModelCode, mdl.ModelNamePrimary"),
            ChartDimension.Colour => (
                "ISNULL(LTRIM(RTRIM(clr.ColourCode))+' - '+LTRIM(RTRIM(clr.ColourName)), 'No Colour')",
                "LEFT JOIN tbl_Colour clr ON t2.fk_ColourID = clr.pk_ColourID",
                "clr.ColourCode, clr.ColourName"),
            ChartDimension.Size => (
                "ISNULL(LTRIM(RTRIM(sz.SizeCode))+' - '+LTRIM(RTRIM(sz.SizeName)), 'No Size')",
                "LEFT JOIN tbl_Size sz ON t2.fk_SizeID = sz.pk_SizeID",
                "sz.SizeCode, sz.SizeName"),
            ChartDimension.SizeGroup => (
                "ISNULL(LTRIM(RTRIM(sg.SizeGroupCode))+' - '+LTRIM(RTRIM(sg.SizeGroupName)), 'No Size Group')",
                "LEFT JOIN tbl_SizeGroup sg ON t2.fk_SizeGroupID = sg.pk_SizeGroupID",
                "sg.SizeGroupCode, sg.SizeGroupName"),
            ChartDimension.Fabric => (
                "ISNULL(LTRIM(RTRIM(fab.FabricCode))+' - '+LTRIM(RTRIM(fab.FabricDescr)), 'No Fabric')",
                "LEFT JOIN tbl_Fabric fab ON t2.fk_FabricID = fab.pk_FabricID",
                "fab.FabricCode, fab.FabricDescr"),
            ChartDimension.Attr1 => (
                "ISNULL(LTRIM(RTRIM(fd1.FieldDetailCode))+' - '+LTRIM(RTRIM(fd1.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd1 ON t2.fk_AttrID1 = fd1.pk_FieldDetailID",
                "fd1.FieldDetailCode, fd1.FieldDetailDescr"),
            ChartDimension.Attr2 => (
                "ISNULL(LTRIM(RTRIM(fd2.FieldDetailCode))+' - '+LTRIM(RTRIM(fd2.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd2 ON t2.fk_AttrID2 = fd2.pk_FieldDetailID",
                "fd2.FieldDetailCode, fd2.FieldDetailDescr"),
            ChartDimension.Attr3 => (
                "ISNULL(LTRIM(RTRIM(fd3.FieldDetailCode))+' - '+LTRIM(RTRIM(fd3.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd3 ON t2.fk_AttrID3 = fd3.pk_FieldDetailID",
                "fd3.FieldDetailCode, fd3.FieldDetailDescr"),
            ChartDimension.Attr4 => (
                "ISNULL(LTRIM(RTRIM(fd4.FieldDetailCode))+' - '+LTRIM(RTRIM(fd4.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd4 ON t2.fk_AttrID4 = fd4.pk_FieldDetailID",
                "fd4.FieldDetailCode, fd4.FieldDetailDescr"),
            ChartDimension.Attr5 => (
                "ISNULL(LTRIM(RTRIM(fd5.FieldDetailCode))+' - '+LTRIM(RTRIM(fd5.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd5 ON t2.fk_AttrID5 = fd5.pk_FieldDetailID",
                "fd5.FieldDetailCode, fd5.FieldDetailDescr"),
            ChartDimension.Attr6 => (
                "ISNULL(LTRIM(RTRIM(fd6.FieldDetailCode))+' - '+LTRIM(RTRIM(fd6.FieldDetailDescr)), 'N/A')",
                "LEFT JOIN tbl_FieldDetail fd6 ON t2.fk_AttrID6 = fd6.pk_FieldDetailID",
                "fd6.FieldDetailCode, fd6.FieldDetailDescr"),
            ChartDimension.CustCat1 => (
                $"ISNULL(LTRIM(RTRIM(cc1.CategoryCode))+' - '+LTRIM(RTRIM(cc1.CategoryDescr)), 'N/A')",
                $"LEFT JOIN {entityTable} e ON {entityFk} = {entityPk} LEFT JOIN tbl_CustCategory cc1 ON e.fk_Category1 = cc1.pk_CategoryID",
                "cc1.CategoryCode, cc1.CategoryDescr"),
            ChartDimension.CustCat2 => (
                $"ISNULL(LTRIM(RTRIM(cc2.CategoryCode))+' - '+LTRIM(RTRIM(cc2.CategoryDescr)), 'N/A')",
                $"LEFT JOIN {entityTable} e ON {entityFk} = {entityPk} LEFT JOIN tbl_CustCategory cc2 ON e.fk_Category2 = cc2.pk_CategoryID",
                "cc2.CategoryCode, cc2.CategoryDescr"),
            ChartDimension.CustCat3 => (
                $"ISNULL(LTRIM(RTRIM(cc3.CategoryCode))+' - '+LTRIM(RTRIM(cc3.CategoryDescr)), 'N/A')",
                $"LEFT JOIN {entityTable} e ON {entityFk} = {entityPk} LEFT JOIN tbl_CustCategory cc3 ON e.fk_Category3 = cc3.pk_CategoryID",
                "cc3.CategoryCode, cc3.CategoryDescr"),
            ChartDimension.HourOfDay => (
                "RIGHT('0'+CAST(DATEPART(HOUR, h.DateTrans) AS VARCHAR(2)),2)+':00'",
                "",
                "DATEPART(HOUR, h.DateTrans)"),
            _ => ("'Unknown'", "", "'Unknown'")
        };
    }

    private static string GetValueExpression(ChartFilter filter)
    {
        if (filter.Metric == ChartMetric.Quantity)
            return "SUM(d.Quantity)";
        if (filter.Metric == ChartMetric.Count)
            return "COUNT(*)";
        return filter.IncludeVat
            ? "SUM(d.Amount - (d.Discount + d.ExtraDiscount) + d.VatAmount)"
            : "SUM(d.Amount - (d.Discount + d.ExtraDiscount))";
    }

    private static string GetRawValueExpression(ChartFilter filter)
    {
        if (filter.Metric == ChartMetric.Quantity)
            return "d.Quantity";
        if (filter.Metric == ChartMetric.Count)
            return "1";
        return filter.IncludeVat
            ? "(d.Amount - (d.Discount + d.ExtraDiscount) + d.VatAmount)"
            : "(d.Amount - (d.Discount + d.ExtraDiscount))";
    }

    private static string BuildStoreWhere(List<string>? storeCodes)
    {
        if (storeCodes == null || storeCodes.Count == 0) return "";
        var list = string.Join(",", storeCodes.Select((_, i) => $"@SC{i}"));
        return $" AND h.fk_StoreCode IN ({list})";
    }

    private static void AddParameters(SqlCommand cmd, ChartFilter filter)
    {
        cmd.Parameters.AddWithValue("@TopN", filter.TopN);
        cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom);
        cmd.Parameters.AddWithValue("@DateTo", filter.DateTo);

        if (filter.CompareLastYear)
        {
            cmd.Parameters.AddWithValue("@LYDateFrom", filter.DateFrom.AddYears(-1));
            cmd.Parameters.AddWithValue("@LYDateTo", filter.DateTo.AddYears(-1));
        }

        if (filter.StoreCodes != null)
            for (int i = 0; i < filter.StoreCodes.Count; i++)
                cmd.Parameters.AddWithValue($"@SC{i}", filter.StoreCodes[i]);

        var (_, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection);
        foreach (var p in dimParams)
        {
            if (!cmd.Parameters.Contains(p.ParameterName))
                cmd.Parameters.Add(p);
        }
    }
}
