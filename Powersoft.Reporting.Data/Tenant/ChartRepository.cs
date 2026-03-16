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

        var sb = new StringBuilder();
        sb.Append($@"
            SELECT TOP(@TopN) {dimSelect} AS Label, {valueExpr} AS Val
            FROM tbl_InvoiceDetail d
            INNER JOIN tbl_InvoiceHeader h ON d.InvoiceNumber = h.InvoiceNumber
            {dimJoin}
            WHERE h.InvoiceDate >= @DateFrom AND h.InvoiceDate <= @DateTo
              AND h.DocType = 'I'
              {storeWhere}
            GROUP BY {dimGroup}
            ORDER BY Val DESC");

        var othersSql = "";
        if (filter.ShowOthers)
        {
            othersSql = $@"
                ;WITH TopN AS (
                    SELECT TOP(@TopN) {dimGroup} AS DimKey
                    FROM tbl_InvoiceDetail d
                    INNER JOIN tbl_InvoiceHeader h ON d.InvoiceNumber = h.InvoiceNumber
                    {dimJoin}
                    WHERE h.InvoiceDate >= @DateFrom AND h.InvoiceDate <= @DateTo
                      AND h.DocType = 'I'
                      {storeWhere}
                    GROUP BY {dimGroup}
                    ORDER BY {valueExpr} DESC
                )
                SELECT 'Others' AS Label, {valueExpr} AS Val
                FROM tbl_InvoiceDetail d
                INNER JOIN tbl_InvoiceHeader h ON d.InvoiceNumber = h.InvoiceNumber
                {dimJoin}
                WHERE h.InvoiceDate >= @DateFrom AND h.InvoiceDate <= @DateTo
                  AND h.DocType = 'I'
                  {storeWhere}
                  AND {dimGroup} NOT IN (SELECT DimKey FROM TopN)";
        }

        string? compareSql = null;
        if (filter.CompareLastYear)
        {
            compareSql = $@"
                SELECT TOP(@TopN) {dimSelect} AS Label, {valueExpr} AS Val
                FROM tbl_InvoiceDetail d
                INNER JOIN tbl_InvoiceHeader h ON d.InvoiceNumber = h.InvoiceNumber
                {dimJoin}
                WHERE h.InvoiceDate >= @LYDateFrom AND h.InvoiceDate <= @LYDateTo
                  AND h.DocType = 'I'
                  {storeWhere}
                GROUP BY {dimGroup}
                ORDER BY Val DESC";
        }

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var results = new List<ChartDataPoint>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sb.ToString();
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

        if (filter.ShowOthers && !string.IsNullOrEmpty(othersSql))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = othersSql;
            cmd.CommandTimeout = 30;
            AddParameters(cmd, filter);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var othersVal = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                if (othersVal != 0)
                    results.Add(new ChartDataPoint { Label = "Others", Value = othersVal });
            }
        }

        if (filter.CompareLastYear && !string.IsNullOrEmpty(compareSql))
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
            }
        }

        return results;
    }

    private static (string select, string join, string group) GetDimensionSql(ChartDimension dim) => dim switch
    {
        ChartDimension.Category => (
            "ISNULL(cat.Description, 'Uncategorized')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode LEFT JOIN tbl_ItemCategory cat ON i.CategoryId = cat.Id",
            "cat.Description"),
        ChartDimension.Store => (
            "ISNULL(h.StoreCode, 'N/A')",
            "",
            "h.StoreCode"),
        ChartDimension.Brand => (
            "ISNULL(i.Brand, 'No Brand')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode",
            "i.Brand"),
        ChartDimension.Customer => (
            "ISNULL(c.Name, 'Unknown')",
            "LEFT JOIN tbl_Customer c ON h.CustomerCode = c.CustomerCode",
            "c.Name"),
        ChartDimension.Item => (
            "ISNULL(i.Description, d.ItemCode)",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode",
            "ISNULL(i.Description, d.ItemCode)"),
        ChartDimension.Supplier => (
            "ISNULL(s.Name, 'Unknown')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode LEFT JOIN tbl_Supplier s ON i.MainSupplier = s.SupplierCode",
            "s.Name"),
        ChartDimension.Department => (
            "ISNULL(dep.Description, 'No Department')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode LEFT JOIN tbl_ItemDepartment dep ON i.DepartmentId = dep.Id",
            "dep.Description"),
        _ => ("'Unknown'", "", "'Unknown'")
    };

    private static string GetValueExpression(ChartFilter filter)
    {
        if (filter.Metric == ChartMetric.Quantity)
            return "SUM(d.Quantity)";
        return filter.IncludeVat
            ? "SUM(d.Quantity * d.UnitPrice)"
            : "SUM(d.NetAmount)";
    }

    private static string BuildStoreWhere(List<string>? storeCodes)
    {
        if (storeCodes == null || storeCodes.Count == 0) return "";
        var list = string.Join(",", storeCodes.Select((_, i) => $"@SC{i}"));
        return $"AND h.StoreCode IN ({list})";
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
        {
            for (int i = 0; i < filter.StoreCodes.Count; i++)
                cmd.Parameters.AddWithValue($"@SC{i}", filter.StoreCodes[i]);
        }
    }
}
