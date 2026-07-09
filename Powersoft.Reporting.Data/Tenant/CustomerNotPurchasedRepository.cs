using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// "Items Not Purchased (by Customer) in X Days" (Report B — George, 2026).
///
/// Definition (all toggle-driven from <see cref="CustomerNotPurchasedFilter"/>):
///  - A row qualifies when the most recent SALE in scope is more than <c>DaysThreshold</c> days
///    before <c>ReferenceDate</c> — i.e. DATEDIFF(DAY, LastSale, RefDate) &gt; Days
///    (mirrors the Power BI DAX DATEDIFF(LastSale, TODAY(), DAY) &gt; StaleDays).
///  - "Sale" = invoice lines only. Credit/return lines are intentionally NOT counted as a purchase
///    (a return is not a buy). Toggle would require a spec change, not present by default.
///  - Customer scope: <c>CustomerCodes</c> (+ exclude mode). Empty = all customers.
///  - <c>IncludeNeverPurchased</c>=false (default): universe is items sold at least once in the window
///    but not recently. =true (item grouping only): also lists items with no in-scope sale at all.
///
/// No legacy 365 parity source exists for this report; semantics follow George's verbal spec.
/// SQL joins/columns verified against Powersoft.CloudAccounting reference
/// (tbl_InvoiceHeader.DateTrans/fk_CustomerCode/fk_StoreCode, tbl_InvoiceDetails.fk_Invoice/fk_ItemID/Quantity,
///  tbl_Item, tbl_ItemCategory, tbl_Customer.pk_CustomerNo).
/// </summary>
public class CustomerNotPurchasedRepository : ICustomerNotPurchasedRepository
{
    private readonly string _connectionString;

    public CustomerNotPurchasedRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Dimension map for the INNER sales CTE (Shape A). Item dims resolve against tbl_Item alias it_dim.
    private static readonly DimensionFilterBuilder.ColumnMap SalesDimCols =
        DimensionFilterBuilder.Default with
        {
            Category = "it_dim.fk_CategoryID",
            Department = "it_dim.fk_DepartmentID",
            Brand = "it_dim.fk_BrandID",
            Season = "it_dim.fk_SeasonID",
            Item = "d.fk_ItemID",
            Store = "h.fk_StoreCode",
            Supplier = "ris_dim.fk_SupplierNo",
            Customer = "",           // customer scope handled explicitly via filter.CustomerCodes
            Model = "it_dim.fk_ModelID",
            Colour = "it_dim.fk_ColourID",
            Size = "it_dim.fk_SizeID",
            ItemTableAlias = "it_dim"
        };

    // Dimension map for the OUTER catalog (Shape B). Item dims resolve against tbl_Item alias it.
    private static readonly DimensionFilterBuilder.ColumnMap CatalogueDimCols =
        DimensionFilterBuilder.Default with
        {
            Category = "it.fk_CategoryID",
            Department = "it.fk_DepartmentID",
            Brand = "it.fk_BrandID",
            Season = "it.fk_SeasonID",
            Item = "it.pk_ItemID",
            Store = "",              // store restricts sales, not the catalogue universe
            Supplier = "ris_cat.fk_SupplierNo",
            Customer = "",
            Model = "it.fk_ModelID",
            Colour = "it.fk_ColourID",
            Size = "it.fk_SizeID",
            ItemTableAlias = "it"
        };

    private static readonly HashSet<string> ValidSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "DaysSinceLastPurchase", "LastPurchaseDate", "ItemCode", "ItemName",
        "TotalQtyInWindow", "LastPurchaseQty", "CustomerName"
    };

    public async Task<PagedResult<CustomerNotPurchasedRow>> GetDataAsync(CustomerNotPurchasedFilter filter)
    {
        var byCustomer = filter.GroupBy == GroupByType.Customer;
        var includeNever = filter.IncludeNeverPurchased && !byCustomer; // never-sold only meaningful at item level

        var (customerWhere, customerParams) = BuildCustomerWhere(filter);
        var (storeWhere, storeParams) = BuildStoreWhere(filter.StoreCodes);
        var supplierFilterOn = filter.ItemsSelection?.Suppliers.HasFilter == true;

        // Item dimension filter applied in exactly ONE place per shape to avoid duplicate parameter names.
        var (dimWhere, dimParams) = includeNever
            ? DimensionFilterBuilder.Build(filter.ItemsSelection, CatalogueDimCols)
            : DimensionFilterBuilder.Build(filter.ItemsSelection, SalesDimCols);

        var keyCols = byCustomer ? "s.CustomerCode, s.ItemId" : "s.ItemId";
        var aggKeyCols = byCustomer ? "CustomerCode, ItemId" : "ItemId";

        var salesSupplierJoin = (!includeNever && supplierFilterOn)
            ? "\n                LEFT JOIN tbl_RelItemSuppliers ris_dim ON d.fk_ItemID = ris_dim.fk_ItemID AND ISNULL(ris_dim.PrimarySupplier, 0) = 1"
            : "";

        var salesCte = $@"
            Sales AS (
                SELECT ISNULL(h.fk_CustomerCode, N'') AS CustomerCode,
                       d.fk_ItemID                    AS ItemId,
                       h.DateTrans                    AS SaleDate,
                       d.Quantity                     AS Qty
                FROM tbl_InvoiceDetails d WITH (NOLOCK)
                INNER JOIN tbl_InvoiceHeader h WITH (NOLOCK) ON d.fk_Invoice = h.pk_InvoiceID
                INNER JOIN tbl_Item it_dim WITH (NOLOCK) ON d.fk_ItemID = it_dim.pk_ItemID{salesSupplierJoin}
                WHERE CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo{customerWhere}{storeWhere}{(includeNever ? "" : dimWhere)}
            )";

        var aggCte = $@"
            Agg AS (
                SELECT {aggKeyCols},
                       MAX(SaleDate) AS LastPurchaseDate,
                       SUM(Qty)      AS TotalQtyInWindow
                FROM Sales s
                GROUP BY {aggKeyCols}
            )";

        var lastDayCte = $@"
            LastDay AS (
                SELECT {aggKeyCols}, CONVERT(DATE, SaleDate) AS SaleDay, SUM(Qty) AS DayQty
                FROM Sales s
                GROUP BY {aggKeyCols}, CONVERT(DATE, SaleDate)
            )";

        var custSelect = byCustomer
            ? "a.CustomerCode AS CustomerCode, ISNULL(CASE WHEN c.Company = 1 THEN c.LastCompanyName ELSE c.FirstName + N' ' + c.LastCompanyName END, N'') AS CustomerName,"
            : "CAST(NULL AS NVARCHAR(20)) AS CustomerCode, CAST(NULL AS NVARCHAR(200)) AS CustomerName,";
        var custJoin = byCustomer ? "\n            LEFT JOIN tbl_Customer c ON a.CustomerCode = c.pk_CustomerNo" : "";

        var ldKeyMatch = byCustomer
            ? "ld.CustomerCode = a.CustomerCode AND ld.ItemId = a.ItemId"
            : "ld.ItemId = a.ItemId";

        var catalogueSupplierJoin = (includeNever && supplierFilterOn)
            ? "\n            LEFT JOIN tbl_RelItemSuppliers ris_cat ON it.pk_ItemID = ris_cat.fk_ItemID AND ISNULL(ris_cat.PrimarySupplier, 0) = 1"
            : "";

        // Outer body differs by shape: Shape A drives off Agg (sold items only);
        // Shape B drives off the item catalogue and LEFT JOINs Agg (includes never-sold items).
        string outerFromWhere;
        if (includeNever)
        {
            outerFromWhere = $@"
            FROM tbl_Item it WITH (NOLOCK)
            LEFT JOIN Agg a ON it.pk_ItemID = a.ItemId
            LEFT JOIN tbl_ItemCategory cat ON it.fk_CategoryID = cat.pk_CategoryID
            LEFT JOIN LastDay ld ON {ldKeyMatch.Replace("a.CustomerCode", "a.CustomerCode")} AND ld.SaleDay = CONVERT(DATE, a.LastPurchaseDate){catalogueSupplierJoin}
            WHERE (a.LastPurchaseDate IS NULL OR DATEDIFF(DAY, a.LastPurchaseDate, @RefDate) > @Days){dimWhere}";
        }
        else
        {
            outerFromWhere = $@"
            FROM Agg a
            INNER JOIN tbl_Item it ON a.ItemId = it.pk_ItemID
            LEFT JOIN tbl_ItemCategory cat ON it.fk_CategoryID = cat.pk_CategoryID{custJoin}
            LEFT JOIN LastDay ld ON {ldKeyMatch} AND ld.SaleDay = CONVERT(DATE, a.LastPurchaseDate)
            WHERE DATEDIFF(DAY, a.LastPurchaseDate, @RefDate) > @Days";
        }

        var orderBy = $"{SanitizeSort(filter.SortColumn)} {(string.Equals(filter.SortDirection, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC")}";

        var sql = $@"
            ;WITH{salesCte},{aggCte},{lastDayCte}
            SELECT {custSelect}
                   it.pk_ItemID AS ItemId,
                   it.ItemCode,
                   ISNULL(it.ItemNamePrimary, it.ItemCode) AS ItemName,
                   cat.CategoryCode,
                   cat.CategoryDescr,
                   a.LastPurchaseDate,
                   CASE WHEN a.LastPurchaseDate IS NULL THEN NULL
                        ELSE DATEDIFF(DAY, a.LastPurchaseDate, @RefDate) END AS DaysSinceLastPurchase,
                   ISNULL(ld.DayQty, 0)            AS LastPurchaseQty,
                   ISNULL(a.TotalQtyInWindow, 0)   AS TotalQtyInWindow,
                   COUNT(*) OVER() AS TotalCount{outerFromWhere}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

        var result = new PagedResult<CustomerNotPurchasedRow>
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;
        cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", filter.DateTo.Date);
        cmd.Parameters.AddWithValue("@RefDate", filter.ReferenceDate.Date);
        cmd.Parameters.AddWithValue("@Days", filter.DaysThreshold);
        cmd.Parameters.AddWithValue("@Skip", Math.Max(0, filter.Skip));
        cmd.Parameters.AddWithValue("@Take", Math.Min(filter.PageSize, filter.MaxRecords));
        foreach (var p in customerParams) cmd.Parameters.Add(p);
        foreach (var p in storeParams) cmd.Parameters.Add(p);
        foreach (var p in dimParams) cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync();
        var oCustCode = reader.GetOrdinal("CustomerCode");
        var oCustName = reader.GetOrdinal("CustomerName");
        var oItemId = reader.GetOrdinal("ItemId");
        var oItemCode = reader.GetOrdinal("ItemCode");
        var oItemName = reader.GetOrdinal("ItemName");
        var oCatCode = reader.GetOrdinal("CategoryCode");
        var oCatDescr = reader.GetOrdinal("CategoryDescr");
        var oLastDate = reader.GetOrdinal("LastPurchaseDate");
        var oDays = reader.GetOrdinal("DaysSinceLastPurchase");
        var oLastQty = reader.GetOrdinal("LastPurchaseQty");
        var oTotalQty = reader.GetOrdinal("TotalQtyInWindow");
        var oTotalCount = reader.GetOrdinal("TotalCount");

        while (await reader.ReadAsync())
        {
            result.TotalCount = reader.GetInt32(oTotalCount);
            result.Items.Add(new CustomerNotPurchasedRow
            {
                CustomerCode = reader.IsDBNull(oCustCode) ? null : reader.GetString(oCustCode),
                CustomerName = reader.IsDBNull(oCustName) ? null : reader.GetString(oCustName),
                ItemId = reader.GetInt64(oItemId),
                ItemCode = reader.IsDBNull(oItemCode) ? string.Empty : reader.GetString(oItemCode),
                ItemName = reader.IsDBNull(oItemName) ? string.Empty : reader.GetString(oItemName),
                CategoryCode = reader.IsDBNull(oCatCode) ? null : reader.GetString(oCatCode),
                CategoryDescr = reader.IsDBNull(oCatDescr) ? null : reader.GetString(oCatDescr),
                LastPurchaseDate = reader.IsDBNull(oLastDate) ? null : reader.GetDateTime(oLastDate),
                DaysSinceLastPurchase = reader.IsDBNull(oDays) ? null : reader.GetInt32(oDays),
                LastPurchaseQty = reader.IsDBNull(oLastQty) ? 0 : reader.GetDecimal(oLastQty),
                TotalQtyInWindow = reader.IsDBNull(oTotalQty) ? 0 : reader.GetDecimal(oTotalQty)
            });
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 15;
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeSort(string? col) =>
        col != null && ValidSortColumns.Contains(col) ? col : "DaysSinceLastPurchase";

    private static (string where, List<SqlParameter> parms) BuildCustomerWhere(CustomerNotPurchasedFilter filter)
    {
        var parms = new List<SqlParameter>();
        if (filter.CustomerCodes is not { Count: > 0 }) return ("", parms);

        var names = new List<string>();
        for (int i = 0; i < filter.CustomerCodes.Count; i++)
        {
            var p = $"@cust{i}";
            names.Add(p);
            parms.Add(new SqlParameter(p, filter.CustomerCodes[i]));
        }
        var op = filter.CustomerExcludeMode ? "NOT IN" : "IN";
        return ($" AND h.fk_CustomerCode {op} ({string.Join(",", names)})", parms);
    }

    private static (string where, List<SqlParameter> parms) BuildStoreWhere(List<string>? storeCodes)
    {
        var parms = new List<SqlParameter>();
        if (storeCodes is not { Count: > 0 }) return ("", parms);

        var names = new List<string>();
        for (int i = 0; i < storeCodes.Count; i++)
        {
            var p = $"@st{i}";
            names.Add(p);
            parms.Add(new SqlParameter(p, storeCodes[i]));
        }
        return ($" AND h.fk_StoreCode IN ({string.Join(",", names)})", parms);
    }
}
