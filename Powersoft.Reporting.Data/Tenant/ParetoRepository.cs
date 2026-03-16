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
        var (codeExpr, nameExpr, joinSql, groupBy) = GetDimensionSql(filter.Dimension);
        var valueExpr = GetValueExpression(filter);
        var storeWhere = BuildStoreWhere(filter.StoreCodes);

        var sql = $@"
            SELECT {codeExpr} AS Code, {nameExpr} AS Name, {valueExpr} AS Val
            FROM tbl_InvoiceDetail d
            INNER JOIN tbl_InvoiceHeader h ON d.InvoiceNumber = h.InvoiceNumber
            {joinSql}
            WHERE h.InvoiceDate >= @DateFrom AND h.InvoiceDate <= @DateTo
              AND h.DocType = 'I'
              {storeWhere}
            GROUP BY {groupBy}
            ORDER BY Val DESC";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var rawData = new List<(string code, string name, decimal value)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.CommandTimeout = 60;
            cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom);
            cmd.Parameters.AddWithValue("@DateTo", filter.DateTo);
            if (filter.StoreCodes != null)
                for (int i = 0; i < filter.StoreCodes.Count; i++)
                    cmd.Parameters.AddWithValue($"@SC{i}", filter.StoreCodes[i]);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rawData.Add((
                    reader.IsDBNull(0) ? "" : reader.GetString(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)
                ));
            }
        }

        var grandTotal = rawData.Sum(r => r.value);
        var result = new ParetoResult { GrandTotal = grandTotal };
        decimal cumulative = 0;
        int rank = 0;

        foreach (var (code, name, value) in rawData)
        {
            rank++;
            cumulative += value;
            var pct = grandTotal > 0 ? (value / grandTotal * 100) : 0;
            var cumPct = grandTotal > 0 ? (cumulative / grandTotal * 100) : 0;

            string classification;
            if (cumPct <= filter.ClassAThreshold)
            {
                classification = "A";
                result.ClassACount++;
                result.ClassAValue += value;
            }
            else if (cumPct <= filter.ClassBThreshold)
            {
                classification = "B";
                result.ClassBCount++;
                result.ClassBValue += value;
            }
            else
            {
                classification = "C";
                result.ClassCCount++;
                result.ClassCValue += value;
            }

            result.Rows.Add(new ParetoRow
            {
                Rank = rank,
                Code = code,
                Name = name,
                Value = value,
                CumulativeValue = cumulative,
                Percentage = Math.Round(pct, 2),
                CumulativePercentage = Math.Round(cumPct, 2),
                Classification = classification
            });
        }

        return result;
    }

    private static (string codeExpr, string nameExpr, string joinSql, string groupBy) GetDimensionSql(ParetoDimension dim) => dim switch
    {
        ParetoDimension.Item => (
            "d.ItemCode",
            "ISNULL(i.Description, d.ItemCode)",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode",
            "d.ItemCode, i.Description"),
        ParetoDimension.Customer => (
            "ISNULL(h.CustomerCode, '')",
            "ISNULL(c.Name, 'Unknown')",
            "LEFT JOIN tbl_Customer c ON h.CustomerCode = c.CustomerCode",
            "h.CustomerCode, c.Name"),
        ParetoDimension.Category => (
            "ISNULL(CAST(i.CategoryId AS VARCHAR), '')",
            "ISNULL(cat.Description, 'Uncategorized')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode LEFT JOIN tbl_ItemCategory cat ON i.CategoryId = cat.Id",
            "i.CategoryId, cat.Description"),
        ParetoDimension.Supplier => (
            "ISNULL(i.MainSupplier, '')",
            "ISNULL(s.Name, 'Unknown')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode LEFT JOIN tbl_Supplier s ON i.MainSupplier = s.SupplierCode",
            "i.MainSupplier, s.Name"),
        ParetoDimension.Brand => (
            "ISNULL(i.Brand, '')",
            "ISNULL(i.Brand, 'No Brand')",
            "LEFT JOIN tbl_Item i ON d.ItemCode = i.ItemCode",
            "i.Brand"),
        _ => ("d.ItemCode", "d.ItemCode", "", "d.ItemCode")
    };

    private static string GetValueExpression(ParetoFilter filter)
    {
        if (filter.Metric == ParetoMetric.Quantity)
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
}
