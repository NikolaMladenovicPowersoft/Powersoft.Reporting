using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class BelowMinStockRepository : IBelowMinStockRepository
{
    private readonly string _connectionString;

    public BelowMinStockRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private static readonly DimensionFilterBuilder.ColumnMap BmsCols = new(
        Category: "t2.fk_CategoryID",
        Department: "t2.fk_DepartmentID",
        Brand: "t2.fk_BrandID",
        Season: "t2.fk_SeasonID",
        Item: "t2.pk_ItemID",
        Store: "ris.fk_StoreCode",
        ItemTableAlias: "t2");

    public async Task<List<BelowMinStockRow>> GetBelowMinStockAsync(BelowMinStockFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();

        // Column names match CloudAccounting tbl_RelItemStore / tbl_Item (see lq_ItemList.dbml):
        // Stock, MinimumStock, fk_Shelf — not StockQty, MinStockQty, Shelf.
        // ItemNamePrimary / ItemNameSecondary — not ItemDescr. Cost — not LastCost.
        sb.Append(@"
            SELECT 
                t2.ItemCode,
                COALESCE(NULLIF(LTRIM(RTRIM(t2.ItemNameSecondary)), ''), t2.ItemNamePrimary, t2.ItemCode) AS ItemName,
                ris.fk_StoreCode AS StoreCode,
                ISNULL(st.StoreName, ris.fk_StoreCode) AS StoreName,
                cat.CategoryDescr AS CategoryName,
                dep.DepartmentDescr AS DepartmentName,
                br.BrandDesc AS BrandName,
                ISNULL(ris.Stock, 0) AS CurrentStock,
                ISNULL(ris.MinimumStock, 0) AS MinimumStock,
                ISNULL(ris.Stock, 0) - ISNULL(ris.MinimumStock, 0) AS Difference,
                t2.Cost AS Cost,
                ISNULL(ris.Stock, 0) * ISNULL(t2.Cost, 0) AS StockValue,
                ris.fk_Shelf AS Shelf
            FROM tbl_RelItemStore ris
            INNER JOIN tbl_Item t2 ON ris.fk_ItemID = t2.pk_ItemID
            LEFT JOIN tbl_Store st ON ris.fk_StoreCode = st.pk_StoreCode
            LEFT JOIN tbl_ItemCategory cat ON t2.fk_CategoryID = cat.pk_CategoryID
            LEFT JOIN tbl_ItemDepartment dep ON t2.fk_DepartmentID = dep.pk_DepartmentID
            LEFT JOIN tbl_Brands br ON t2.fk_BrandID = br.pk_BrandID
            WHERE ISNULL(ris.MinimumStock, 0) > 0
              AND ISNULL(ris.Stock, 0) < ISNULL(ris.MinimumStock, 0)");

        if (filter.StoreCodes is { Count: > 0 })
        {
            var storeParams = new List<string>();
            for (int i = 0; i < filter.StoreCodes.Count; i++)
            {
                var p = $"@st{i}";
                storeParams.Add(p);
                parms.Add(new SqlParameter(p, filter.StoreCodes[i]));
            }
            sb.Append($" AND ris.fk_StoreCode IN ({string.Join(",", storeParams)})");
        }

        var (dimWhere, dimParms) = DimensionFilterBuilder.Build(filter.ItemsSelection, BmsCols);
        if (!string.IsNullOrEmpty(dimWhere))
        {
            sb.Append(dimWhere);
            parms.AddRange(dimParms);
        }

        var sortCol = ResolveSortColumn(filter.SortColumn);
        var sortDir = filter.SortDirection?.ToUpperInvariant() == "DESC" ? "DESC" : "ASC";
        sb.Append($" ORDER BY {sortCol} {sortDir}");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sb.ToString(), conn);
        cmd.Parameters.AddRange(parms.ToArray());

        var results = new List<BelowMinStockRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new BelowMinStockRow
            {
                ItemCode = reader.GetString(0),
                ItemName = reader.GetString(1),
                StoreCode = reader.GetString(2),
                StoreName = reader.GetString(3),
                CategoryName = reader.IsDBNull(4) ? null : reader.GetString(4),
                DepartmentName = reader.IsDBNull(5) ? null : reader.GetString(5),
                BrandName = reader.IsDBNull(6) ? null : reader.GetString(6),
                CurrentStock = reader.GetDecimal(7),
                MinimumStock = reader.GetDecimal(8),
                Difference = reader.GetDecimal(9),
                Cost = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                StockValue = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                Shelf = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        return results;
    }

    private static string ResolveSortColumn(string? col) => col?.ToLowerInvariant() switch
    {
        "itemname" => "t2.ItemNamePrimary",
        "storecode" => "ris.fk_StoreCode",
        "storename" => "st.StoreName",
        "category" => "cat.CategoryDescr",
        "department" => "dep.DepartmentDescr",
        "brand" => "br.BrandDesc",
        "currentstock" => "ris.Stock",
        "minimumstock" => "ris.MinimumStock",
        "difference" => "Difference",
        "cost" => "t2.Cost",
        "stockvalue" => "StockValue",
        "shelf" => "ris.fk_Shelf",
        _ => "t2.ItemCode"
    };
}
