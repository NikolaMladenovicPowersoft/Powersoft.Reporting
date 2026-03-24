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
        var (dimSelect, dimJoin, dimGroup) = GetDimensionSql(filter.Dimension);
        var valueExpr = GetValueExpression(filter);
        var storeWhere = BuildStoreWhere(filter.StoreCodes);
        var (dimWhere, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection);

        var mainSql = $@"
            SELECT TOP(@TopN) {dimSelect} AS Label, {valueExpr} AS Val
            FROM tbl_InvoiceDetails t1
            INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
            INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID
            {dimJoin}
            WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo
              {storeWhere}{dimWhere}
            GROUP BY {dimGroup}
            ORDER BY Val DESC";

        string? totalSql = null;
        if (filter.ShowOthers)
        {
            totalSql = $@"
                SELECT {valueExpr} AS Val
                FROM tbl_InvoiceDetails t1
                INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
                INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID
                {dimJoin}
                WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo
                  {storeWhere}{dimWhere}";
        }

        string? compareSql = null;
        if (filter.CompareLastYear)
        {
            compareSql = $@"
                SELECT TOP(@TopN) {dimSelect} AS Label, {valueExpr} AS Val
                FROM tbl_InvoiceDetails t1
                INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
                INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID
                {dimJoin}
                WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @LYDateFrom AND @LYDateTo
                  {storeWhere}{dimWhere}
                GROUP BY {dimGroup}
                ORDER BY Val DESC";
        }

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
                pt.CompareValue = lyData.GetValueOrDefault(pt.Label, 0);
        }

        return results;
    }

    private static (string select, string join, string group) GetDimensionSql(ChartDimension dim) => dim switch
    {
        ChartDimension.Category => (
            "ISNULL(LTRIM(RTRIM(cat.CategoryCode))+' - '+LTRIM(RTRIM(cat.CategoryDescr)), 'Uncategorized')",
            "LEFT JOIN tbl_ItemCategory cat ON t2.fk_CategoryID = cat.pk_CategoryID",
            "cat.CategoryCode, cat.CategoryDescr"),
        ChartDimension.Store => (
            "ISNULL(LTRIM(RTRIM(st.pk_StoreCode))+' - '+LTRIM(RTRIM(st.StoreName)), 'N/A')",
            "LEFT JOIN tbl_Store st ON t3.fk_StoreCode = st.pk_StoreCode",
            "st.pk_StoreCode, st.StoreName"),
        ChartDimension.Brand => (
            "ISNULL(LTRIM(RTRIM(br.BrandCode))+' - '+LTRIM(RTRIM(br.BrandDesc)), 'No Brand')",
            "LEFT JOIN tbl_Brands br ON t2.fk_BrandID = br.pk_BrandID",
            "br.BrandCode, br.BrandDesc"),
        ChartDimension.Customer => (
            "ISNULL(CASE WHEN c.Company=1 THEN c.LastCompanyName ELSE c.FirstName+' '+c.LastCompanyName END, 'Unknown')",
            "LEFT JOIN tbl_Customer c ON t3.fk_CustomerCode = c.pk_CustomerNo",
            "c.Company, c.FirstName, c.LastCompanyName"),
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
        _ => ("'Unknown'", "", "'Unknown'")
    };

    private static string GetValueExpression(ChartFilter filter)
    {
        if (filter.Metric == ChartMetric.Quantity)
            return "SUM(t1.Quantity)";
        return filter.IncludeVat
            ? "SUM(t1.Amount - (t1.Discount + t1.ExtraDiscount) + t1.VatAmount)"
            : "SUM(t1.Amount - (t1.Discount + t1.ExtraDiscount))";
    }

    private static string BuildStoreWhere(List<string>? storeCodes)
    {
        if (storeCodes == null || storeCodes.Count == 0) return "";
        var list = string.Join(",", storeCodes.Select((_, i) => $"@SC{i}"));
        return $"AND t3.fk_StoreCode IN ({list})";
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
