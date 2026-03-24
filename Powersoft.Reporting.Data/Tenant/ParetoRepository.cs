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
        var (dimWhere, dimParams) = DimensionFilterBuilder.Build(filter.ItemsSelection);

        var sql = $@"
            SELECT {codeExpr} AS Code, {nameExpr} AS Name, {valueExpr} AS Val
            FROM tbl_InvoiceDetails t1
            INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
            INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID
            {joinSql}
            WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo
              {storeWhere}{dimWhere}
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
            foreach (var p in dimParams)
                cmd.Parameters.Add(p);

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
            "t2.ItemCode",
            "ISNULL(t2.ItemNamePrimary, t2.ItemCode)",
            "",
            "t2.ItemCode, t2.ItemNamePrimary"),
        ParetoDimension.Customer => (
            "ISNULL(t3.fk_CustomerCode, '')",
            "ISNULL(CASE WHEN c.Company=1 THEN c.LastCompanyName ELSE c.FirstName+' '+c.LastCompanyName END, 'Unknown')",
            "LEFT JOIN tbl_Customer c ON t3.fk_CustomerCode = c.pk_CustomerNo",
            "t3.fk_CustomerCode, c.Company, c.FirstName, c.LastCompanyName"),
        ParetoDimension.Category => (
            "ISNULL(cat.CategoryCode, 'N/A')",
            "ISNULL(LTRIM(RTRIM(cat.CategoryCode))+' - '+LTRIM(RTRIM(cat.CategoryDescr)), 'Uncategorized')",
            "LEFT JOIN tbl_ItemCategory cat ON t2.fk_CategoryID = cat.pk_CategoryID",
            "cat.CategoryCode, cat.CategoryDescr"),
        ParetoDimension.Supplier => (
            "ISNULL(sup.pk_SupplierNo, '')",
            "ISNULL(CASE WHEN sup.Company=1 THEN sup.LastCompanyName ELSE sup.FirstName+' '+sup.LastCompanyName END, 'Unknown')",
            "LEFT JOIN tbl_RelItemSuppliers ris ON t2.pk_ItemID = ris.fk_ItemID AND ISNULL(ris.PrimarySupplier,0)=1 LEFT JOIN tbl_Supplier sup ON ris.fk_SupplierNo = sup.pk_SupplierNo",
            "sup.pk_SupplierNo, sup.Company, sup.FirstName, sup.LastCompanyName"),
        ParetoDimension.Brand => (
            "ISNULL(br.BrandCode, 'N/A')",
            "ISNULL(LTRIM(RTRIM(br.BrandCode))+' - '+LTRIM(RTRIM(br.BrandDesc)), 'No Brand')",
            "LEFT JOIN tbl_Brands br ON t2.fk_BrandID = br.pk_BrandID",
            "br.BrandCode, br.BrandDesc"),
        _ => ("t2.ItemCode", "t2.ItemCode", "", "t2.ItemCode")
    };

    private static string GetValueExpression(ParetoFilter filter)
    {
        if (filter.Metric == ParetoMetric.Quantity)
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
}
