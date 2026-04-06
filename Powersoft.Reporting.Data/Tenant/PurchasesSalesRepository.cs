using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class PurchasesSalesRepository : IPurchasesSalesRepository
{
    private readonly string _connectionString;

    public PurchasesSalesRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PagedResult<PurchasesSalesRow>> GetPurchasesSalesDataAsync(PurchasesSalesFilter filter)
    {
        var grouping = BuildGrouping(filter);
        var itemFilters = BuildItemFilters(filter);
        var isSummary = filter.IsSummary;

        var innerUnion = BuildUnionAllSubquery(filter, itemFilters);
        var outerSql = BuildOuterQuery(filter, grouping, innerUnion, isSummary);

        var (colFilterWhere, colFilterParams) = BuildColumnFilterClause(filter);

        var dataSql = $@";WITH Data AS ({outerSql})
SELECT * FROM Data d{colFilterWhere}
ORDER BY {ResolveSortExpression(filter)}
OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@";WITH Data AS ({outerSql})
SELECT COUNT(*) FROM Data d{colFilterWhere}";

        var totalsSql = BuildTotalsQuery(filter, itemFilters);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var allParams = BuildCommonParameters(filter, itemFilters);
        allParams.AddRange(colFilterParams);

        int totalCount = await ExecuteCountAsync(conn, countSql, allParams, filter);
        var items = await ExecuteDataAsync(conn, dataSql, allParams, filter, grouping);
        var totals = await ExecuteTotalsAsync(conn, totalsSql, filter, itemFilters);

        return new PagedResult<PurchasesSalesRow>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            PsTotals = totals
        };
    }

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
        catch { return false; }
    }

    #region SQL Generation

    private string BuildUnionAllSubquery(PurchasesSalesFilter filter, ItemFilterInfo itemFilters)
    {
        var dateWhere = "WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo";
        var selCond = itemFilters.WhereClause;

        var sb = new StringBuilder();

        sb.AppendLine("SELECT t2.ItemCode, t2.ItemNamePrimary");
        AppendHeaderFields(sb, filter);
        sb.AppendLine(",SUM(t1.Quantity) AS QuantityPurchased");
        sb.AppendLine(",SUM(t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + (ISNULL(cost.CostAmount,0)*ISNULL(cost.Quantity,0))) AS NetPurchasedValue");
        sb.AppendLine(",SUM(t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)) AS GrossPurchasedValue");
        sb.AppendLine(",0.00 AS QuantitySold, 0.00 AS NetSoldValue, 0.00 AS GrossSoldValue");
        sb.AppendLine($",{GetStockColumn(filter)} AS TotalStockQty");
        sb.AppendLine("FROM tbl_PurchInvoiceDetails t1");
        sb.AppendLine("INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
        sb.AppendLine("INNER JOIN tbl_PurchInvoiceHeader t3 ON t1.fk_PurchInvoiceID = t3.pk_PurchInvoiceID");
        sb.AppendLine("LEFT JOIN tbl_CostingDetails cost ON t1.pk_ID = cost.fk_ID");
        AppendModelSupplierJoins(sb);
        AppendStoreStockJoin(sb, filter);
        sb.AppendLine($"{dateWhere}{selCond}");
        sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, {GetStockColumn(filter)}{AppendHeaderGroupBy(filter)}");

        sb.AppendLine("UNION ALL");

        sb.AppendLine("SELECT t2.ItemCode, t2.ItemNamePrimary");
        AppendHeaderFields(sb, filter);
        sb.AppendLine(",SUM(t1.Quantity * (-1)) AS QuantityPurchased");
        sb.AppendLine(",SUM((t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0))) * (-1)) AS NetPurchasedValue");
        sb.AppendLine(",SUM((t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)) * (-1)) AS GrossPurchasedValue");
        sb.AppendLine(",0.00 AS QuantitySold, 0.00 AS NetSoldValue, 0.00 AS GrossSoldValue");
        sb.AppendLine($",{GetStockColumn(filter)} AS TotalStockQty");
        sb.AppendLine("FROM tbl_PurchReturnDetails t1");
        sb.AppendLine("INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
        sb.AppendLine("INNER JOIN tbl_PurchReturnHeader t3 ON t1.fk_PurchReturnID = t3.pk_PurchReturnID");
        AppendModelSupplierJoins(sb);
        AppendStoreStockJoin(sb, filter);
        sb.AppendLine($"{dateWhere}{selCond}");
        sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, {GetStockColumn(filter)}{AppendHeaderGroupBy(filter)}");

        sb.AppendLine("UNION ALL");

        sb.AppendLine("SELECT t2.ItemCode, t2.ItemNamePrimary");
        AppendHeaderFields(sb, filter);
        sb.AppendLine(",0.00 AS QuantityPurchased, 0.00 AS NetPurchasedValue, 0.00 AS GrossPurchasedValue");
        sb.AppendLine(",SUM(t1.Quantity) AS QuantitySold");
        sb.AppendLine(",SUM(t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0))) AS NetSoldValue");
        sb.AppendLine(",SUM(t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)) AS GrossSoldValue");
        sb.AppendLine($",{GetStockColumn(filter)} AS TotalStockQty");
        sb.AppendLine("FROM tbl_InvoiceDetails t1");
        sb.AppendLine("INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
        sb.AppendLine("INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID");
        AppendModelSupplierJoins(sb);
        AppendStoreStockJoin(sb, filter);
        sb.AppendLine($"{dateWhere}{selCond}");
        sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, {GetStockColumn(filter)}{AppendHeaderGroupBy(filter)}");

        sb.AppendLine("UNION ALL");

        sb.AppendLine("SELECT t2.ItemCode, t2.ItemNamePrimary");
        AppendHeaderFields(sb, filter);
        sb.AppendLine(",0.00 AS QuantityPurchased, 0.00 AS NetPurchasedValue, 0.00 AS GrossPurchasedValue");
        sb.AppendLine(",SUM(t1.Quantity * (-1)) AS QuantitySold");
        sb.AppendLine(",SUM((t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0))) * (-1)) AS NetSoldValue");
        sb.AppendLine(",SUM((t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)) * (-1)) AS GrossSoldValue");
        sb.AppendLine($",{GetStockColumn(filter)} AS TotalStockQty");
        sb.AppendLine("FROM tbl_CreditDetails t1");
        sb.AppendLine("INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
        sb.AppendLine("INNER JOIN tbl_CreditHeader t3 ON t1.fk_Credit = t3.pk_CreditID");
        AppendModelSupplierJoins(sb);
        AppendStoreStockJoin(sb, filter);
        sb.AppendLine($"{dateWhere}{selCond}");
        sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, {GetStockColumn(filter)}{AppendHeaderGroupBy(filter)}");

        return sb.ToString();
    }

    private void AppendHeaderFields(StringBuilder sb, PurchasesSalesFilter filter)
    {
        if (filter.HasStoreFilter || NeedsStoreJoin(filter))
            sb.AppendLine(",t3.fk_StoreCode");
    }

    private string AppendHeaderGroupBy(PurchasesSalesFilter filter)
    {
        if (filter.HasStoreFilter || NeedsStoreJoin(filter))
            return ", t3.fk_StoreCode";
        return "";
    }

    private static void AppendModelSupplierJoins(StringBuilder sb)
    {
        sb.AppendLine("LEFT JOIN tbl_Model t5 ON t2.fk_ModelID = t5.pk_ModelID");
        sb.AppendLine("LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID = t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0) = 1");
    }

    private string GetStockColumn(PurchasesSalesFilter filter) =>
        NeedsStoreJoin(filter) ? "ISNULL(stk.Stock, 0)" : "t2.TotalStockQty";

    private void AppendStoreStockJoin(StringBuilder sb, PurchasesSalesFilter filter)
    {
        if (NeedsStoreJoin(filter))
            sb.AppendLine("LEFT JOIN tbl_RelItemStore stk ON t2.pk_ItemID = stk.fk_ItemID AND t3.fk_StoreCode = stk.fk_StoreCode");
    }

    private bool NeedsStoreJoin(PurchasesSalesFilter filter)
    {
        return filter.PrimaryGroup == PsGroupBy.Store
            || filter.SecondaryGroup == PsGroupBy.Store
            || filter.ThirdGroup == PsGroupBy.Store;
    }

    private string BuildOuterQuery(PurchasesSalesFilter filter, GroupingInfo grouping, string innerUnion, bool isSummary)
    {
        var needsStore = NeedsStoreJoin(filter);
        var storeCol = needsStore ? "fk_StoreCode," : "";
        var storeGroupBy = needsStore ? ", fk_StoreCode" : "";

        var perItemSql = $@"SELECT ItemCode, ItemNamePrimary, {storeCol}
  SUM(QuantityPurchased) AS QuantityPurchased,
  SUM(NetPurchasedValue) AS NetPurchasedValue,
  SUM(GrossPurchasedValue) AS GrossPurchasedValue,
  SUM(QuantitySold) AS QuantitySold,
  SUM(NetSoldValue) AS NetSoldValue,
  SUM(GrossSoldValue) AS GrossSoldValue,
  MAX(TotalStockQty) AS TotalStockQty
FROM ({innerUnion}) raw
GROUP BY ItemCode, ItemNamePrimary{storeGroupBy}";

        var sb = new StringBuilder();

        sb.AppendLine("SELECT");
        sb.AppendLine($"  {grouping.Level1Select},");
        sb.AppendLine($"  {grouping.Level2Select},");
        sb.AppendLine($"  {grouping.Level3Select},");

        bool allNone = filter.PrimaryGroup == PsGroupBy.None
                    && filter.SecondaryGroup == PsGroupBy.None
                    && filter.ThirdGroup == PsGroupBy.None;

        if (!isSummary || allNone)
        {
            sb.AppendLine("  tf.ItemCode,");
            sb.AppendLine("  tf.ItemNamePrimary,");
        }
        else
        {
            sb.AppendLine("  NULL AS ItemCode,");
            sb.AppendLine("  NULL AS ItemNamePrimary,");
        }

        sb.AppendLine("  SUM(tf.QuantityPurchased) AS QuantityPurchased,");
        sb.AppendLine("  SUM(tf.NetPurchasedValue) AS NetPurchasedValue,");
        sb.AppendLine("  SUM(tf.GrossPurchasedValue) AS GrossPurchasedValue,");
        sb.AppendLine("  SUM(tf.QuantitySold) AS QuantitySold,");
        sb.AppendLine("  SUM(tf.NetSoldValue) AS NetSoldValue,");
        sb.AppendLine("  SUM(tf.GrossSoldValue) AS GrossSoldValue,");
        sb.AppendLine("  SUM(tf.NetSoldValue - tf.NetPurchasedValue) AS Profit,");

        if (isSummary && !allNone)
            sb.AppendLine("  SUM(tf.TotalStockQty) AS TotalStockQty");
        else
            sb.AppendLine("  tf.TotalStockQty");

        sb.AppendLine($"FROM ({perItemSql}) tf");

        if (grouping.HasAnyGroup)
        {
            sb.AppendLine("INNER JOIN tbl_Item it ON tf.ItemCode = it.ItemCode");
            sb.AppendLine("LEFT JOIN tbl_RelItemSuppliers s ON it.pk_ItemID = s.fk_ItemID AND ISNULL(s.PrimarySupplier,0) = 1");
            sb.AppendLine("LEFT JOIN tbl_Model m ON it.fk_ModelID = m.pk_ModelID");
        }

        if (!string.IsNullOrEmpty(grouping.JoinClause))
            sb.AppendLine(grouping.JoinClause);

        var groupByCols = new List<string>();
        if (grouping.HasAnyGroup)
            groupByCols.AddRange(grouping.GroupByFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (!isSummary || allNone)
        {
            groupByCols.Add("tf.ItemCode");
            groupByCols.Add("tf.ItemNamePrimary");
        }

        if (!(isSummary && !allNone))
            groupByCols.Add("tf.TotalStockQty");

        if (needsStore)
            groupByCols.Add("tf.fk_StoreCode");

        if (groupByCols.Any())
            sb.AppendLine($"GROUP BY {string.Join(", ", groupByCols)}");

        return sb.ToString();
    }

    private string BuildTotalsQuery(PurchasesSalesFilter filter, ItemFilterInfo itemFilters)
    {
        var dateWhere = "WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo";
        var selCond = itemFilters.WhereClause;

        return $@"
SELECT
  SUM(sub.QuantityPurchased) AS TotalQtyPurchased,
  SUM(sub.NetPurchasedValue) AS TotalNetPurchased,
  SUM(sub.GrossPurchasedValue) AS TotalGrossPurchased,
  SUM(sub.QuantitySold) AS TotalQtySold,
  SUM(sub.NetSoldValue) AS TotalNetSold,
  SUM(sub.GrossSoldValue) AS TotalGrossSold,
  SUM(sub.TotalStockQty) AS TotalStockQty
FROM (
  SELECT t2.ItemCode,
         SUM(t1.Quantity) AS QuantityPurchased,
         SUM(t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+(ISNULL(cost.CostAmount,0)*ISNULL(cost.Quantity,0))) AS NetPurchasedValue,
         SUM(t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+ISNULL(t1.VatAmount,0)) AS GrossPurchasedValue,
         0.00 AS QuantitySold, 0.00 AS NetSoldValue, 0.00 AS GrossSoldValue, MAX(t2.TotalStockQty) AS TotalStockQty
  FROM tbl_PurchInvoiceDetails t1
  INNER JOIN tbl_Item t2 ON t1.fk_ItemID=t2.pk_ItemID
  INNER JOIN tbl_PurchInvoiceHeader t3 ON t1.fk_PurchInvoiceID=t3.pk_PurchInvoiceID
  LEFT JOIN tbl_CostingDetails cost ON t1.pk_ID=cost.fk_ID
  LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID=t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0)=1
  {dateWhere}{selCond}
  GROUP BY t2.ItemCode
  UNION ALL
  SELECT t2.ItemCode,
         SUM(t1.Quantity*(-1)),SUM((t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)))*(-1)),
         SUM((t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+ISNULL(t1.VatAmount,0))*(-1)),
         0.00,0.00,0.00,0.00
  FROM tbl_PurchReturnDetails t1
  INNER JOIN tbl_Item t2 ON t1.fk_ItemID=t2.pk_ItemID
  INNER JOIN tbl_PurchReturnHeader t3 ON t1.fk_PurchReturnID=t3.pk_PurchReturnID
  LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID=t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0)=1
  {dateWhere}{selCond}
  GROUP BY t2.ItemCode
  UNION ALL
  SELECT t2.ItemCode,
         0.00,0.00,0.00,SUM(t1.Quantity),
         SUM(t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))),
         SUM(t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+ISNULL(t1.VatAmount,0)),0.00
  FROM tbl_InvoiceDetails t1
  INNER JOIN tbl_Item t2 ON t1.fk_ItemID=t2.pk_ItemID
  INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice=t3.pk_InvoiceID
  LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID=t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0)=1
  {dateWhere}{selCond}
  GROUP BY t2.ItemCode
  UNION ALL
  SELECT t2.ItemCode,
         0.00,0.00,0.00,SUM(t1.Quantity*(-1)),
         SUM((t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)))*(-1)),
         SUM((t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+ISNULL(t1.VatAmount,0))*(-1)),0.00
  FROM tbl_CreditDetails t1
  INNER JOIN tbl_Item t2 ON t1.fk_ItemID=t2.pk_ItemID
  INNER JOIN tbl_CreditHeader t3 ON t1.fk_Credit=t3.pk_CreditID
  LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID=t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0)=1
  {dateWhere}{selCond}
  GROUP BY t2.ItemCode
) sub";
    }

    public async Task<List<PurchasesSalesMonthlyRow>> GetPurchasesSalesMonthlyAsync(PurchasesSalesFilter filter)
    {
        var grouping = BuildGrouping(filter);
        var itemFilters = BuildItemFilters(filter);
        var isSummary = filter.PrimaryGroup != PsGroupBy.None || filter.SecondaryGroup != PsGroupBy.None;

        var sql = BuildMonthlyQuery(filter, grouping, itemFilters, isSummary);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var parms = BuildCommonParameters(filter, itemFilters);
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parms) cmd.Parameters.Add(Clone(p));

        var results = new List<PurchasesSalesMonthlyRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new PurchasesSalesMonthlyRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i)) continue;
                var name = reader.GetName(i);
                switch (name)
                {
                    case "Level1": row.Level1 = reader.GetString(i); break;
                    case "Level1Value": row.Level1Value = reader.GetString(i); break;
                    case "Level2": row.Level2 = reader.GetString(i); break;
                    case "Level2Value": row.Level2Value = reader.GetString(i); break;
                    case "ItemCode": row.ItemCode = reader.GetString(i); break;
                    case "ItemNamePrimary": row.ItemName = reader.GetString(i); break;
                    case "transYear": row.TransYear = reader.GetInt32(i); break;
                    default:
                        if (name.StartsWith("Purchased_") && int.TryParse(name[10..], out var pm) && pm >= 1 && pm <= 12)
                            row.Purchased[pm - 1] = reader.GetDecimal(i);
                        else if (name.StartsWith("Sold_") && int.TryParse(name[5..], out var sm) && sm >= 1 && sm <= 12)
                            row.Sold[sm - 1] = reader.GetDecimal(i);
                        break;
                }
            }
            results.Add(row);
        }
        return results;
    }

    private string BuildMonthlyQuery(PurchasesSalesFilter filter, GroupingInfo grouping, ItemFilterInfo itemFilters, bool isSummary)
    {
        var dateWhere = "WHERE CONVERT(DATE, t3.DateTrans) BETWEEN @DateFrom AND @DateTo";
        var selCond = itemFilters.WhereClause;
        bool useGross = filter.IncludeVat;
        string valExpr = useGross
            ? "t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)"
            : "t1.Amount - (ISNULL(t1.Discount,0) + ISNULL(t1.ExtraDiscount,0))";

        string MC(string expr, int m) => $"SUM(CASE WHEN MONTH(t3.DateTrans) = {m} THEN {expr} ELSE 0 END)";

        var sb = new StringBuilder();

        void AppendPurchLeg(string detailTbl, string headerTbl, string headerJoin, string sign, bool hasCost)
        {
            sb.AppendLine($"SELECT t2.ItemCode, t2.ItemNamePrimary, YEAR(t3.DateTrans) AS transYear");
            if (NeedsStoreJoin(filter)) sb.AppendLine(",t3.fk_StoreCode");
            var costAdd = hasCost ? "+(ISNULL(cost.CostAmount,0)*ISNULL(cost.Quantity,0))" : "";
            var vExpr = useGross ? $"t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))+ISNULL(t1.VatAmount,0){costAdd}" 
                                 : $"t1.Amount-(ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)){costAdd}";
            for (int m = 1; m <= 12; m++)
            {
                var e = sign == "-" ? $"({vExpr})*(-1)" : vExpr;
                sb.AppendLine($",{MC(e, m)} AS pv_{m}");
            }
            for (int m = 1; m <= 12; m++) sb.AppendLine($",0.00 AS sv_{m}");
            sb.AppendLine($"FROM {detailTbl} t1");
            sb.AppendLine($"INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
            sb.AppendLine($"INNER JOIN {headerTbl} t3 ON {headerJoin}");
            if (hasCost) sb.AppendLine("LEFT JOIN tbl_CostingDetails cost ON t1.pk_ID = cost.fk_ID");
            AppendModelSupplierJoins(sb);
            sb.AppendLine($"{dateWhere}{selCond}");
            var sg = NeedsStoreJoin(filter) ? ", t3.fk_StoreCode" : "";
            sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, YEAR(t3.DateTrans){sg}");
        }

        void AppendSaleLeg(string detailTbl, string headerTbl, string headerJoin, string sign)
        {
            sb.AppendLine($"SELECT t2.ItemCode, t2.ItemNamePrimary, YEAR(t3.DateTrans) AS transYear");
            if (NeedsStoreJoin(filter)) sb.AppendLine(",t3.fk_StoreCode");
            for (int m = 1; m <= 12; m++) sb.AppendLine($",0.00 AS pv_{m}");
            for (int m = 1; m <= 12; m++)
            {
                var e = sign == "-" ? $"({valExpr})*(-1)" : valExpr;
                sb.AppendLine($",{MC(e, m)} AS sv_{m}");
            }
            sb.AppendLine($"FROM {detailTbl} t1");
            sb.AppendLine($"INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID");
            sb.AppendLine($"INNER JOIN {headerTbl} t3 ON {headerJoin}");
            AppendModelSupplierJoins(sb);
            sb.AppendLine($"{dateWhere}{selCond}");
            var sg = NeedsStoreJoin(filter) ? ", t3.fk_StoreCode" : "";
            sb.AppendLine($"GROUP BY t2.ItemCode, t2.ItemNamePrimary, YEAR(t3.DateTrans){sg}");
        }

        AppendPurchLeg("tbl_PurchInvoiceDetails", "tbl_PurchInvoiceHeader",
            "t1.fk_PurchInvoiceID = t3.pk_PurchInvoiceID", "+", true);
        sb.AppendLine("UNION ALL");
        AppendPurchLeg("tbl_PurchReturnDetails", "tbl_PurchReturnHeader",
            "t1.fk_PurchReturnID = t3.pk_PurchReturnID", "-", false);
        sb.AppendLine("UNION ALL");
        AppendSaleLeg("tbl_InvoiceDetails", "tbl_InvoiceHeader",
            "t1.fk_Invoice = t3.pk_InvoiceID", "+");
        sb.AppendLine("UNION ALL");
        AppendSaleLeg("tbl_CreditDetails", "tbl_CreditHeader",
            "t1.fk_Credit = t3.pk_CreditID", "-");

        var innerUnion = sb.ToString();
        sb.Clear();

        var needsStore = NeedsStoreJoin(filter);
        var storeCol = needsStore ? "fk_StoreCode," : "";
        var storeGb2 = needsStore ? ", fk_StoreCode" : "";

        var sumCols = new StringBuilder();
        for (int m = 1; m <= 12; m++) sumCols.AppendLine($"  SUM(pv_{m}) AS pv_{m},");
        for (int m = 1; m <= 12; m++) { sumCols.Append($"  SUM(sv_{m}) AS sv_{m}"); sumCols.AppendLine(m < 12 ? "," : ""); }

        var perItemSql = $@"SELECT ItemCode, ItemNamePrimary, transYear, {storeCol}
{sumCols}
FROM ({innerUnion}) raw
GROUP BY ItemCode, ItemNamePrimary, transYear{storeGb2}";

        sb.AppendLine("SELECT");
        sb.AppendLine($"  {grouping.Level1Select},");
        sb.AppendLine($"  {grouping.Level2Select},");
        if (!isSummary) { sb.AppendLine("  tf.ItemCode,"); sb.AppendLine("  tf.ItemNamePrimary,"); }
        else { sb.AppendLine("  NULL AS ItemCode,"); sb.AppendLine("  NULL AS ItemNamePrimary,"); }
        sb.AppendLine("  tf.transYear,");
        for (int m = 1; m <= 12; m++) sb.AppendLine($"  SUM(tf.pv_{m}) AS Purchased_{m},");
        for (int m = 1; m <= 12; m++) { sb.Append($"  SUM(tf.sv_{m}) AS Sold_{m}"); sb.AppendLine(m < 12 ? "," : ""); }

        sb.AppendLine($"FROM ({perItemSql}) tf");

        if (grouping.HasAnyGroup)
        {
            sb.AppendLine("INNER JOIN tbl_Item it ON tf.ItemCode = it.ItemCode");
            sb.AppendLine("LEFT JOIN tbl_RelItemSuppliers s ON it.pk_ItemID = s.fk_ItemID AND ISNULL(s.PrimarySupplier,0) = 1");
            sb.AppendLine("LEFT JOIN tbl_Model m ON it.fk_ModelID = m.pk_ModelID");
        }
        if (!string.IsNullOrEmpty(grouping.JoinClause)) sb.AppendLine(grouping.JoinClause);

        var groupByCols = new List<string>();
        if (grouping.HasAnyGroup)
            groupByCols.AddRange(grouping.GroupByFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!isSummary) { groupByCols.Add("tf.ItemCode"); groupByCols.Add("tf.ItemNamePrimary"); }
        groupByCols.Add("tf.transYear");
        if (needsStore) groupByCols.Add("tf.fk_StoreCode");
        sb.AppendLine($"GROUP BY {string.Join(", ", groupByCols)}");

        var sortParts = new List<string>();
        if (filter.PrimaryGroup != PsGroupBy.None) { sortParts.Add("Level1Value"); sortParts.Add("Level1"); }
        if (filter.SecondaryGroup != PsGroupBy.None) { sortParts.Add("Level2Value"); sortParts.Add("Level2"); }
        sortParts.Add("transYear");
        if (!isSummary) sortParts.Add("ItemCode");
        sb.AppendLine($"ORDER BY {string.Join(", ", sortParts)}");

        return sb.ToString();
    }

    #endregion

    #region Grouping

    private record GroupingInfo(
        string Level1Select, string Level2Select, string Level3Select,
        string JoinClause, string GroupByFields, bool HasAnyGroup);

    private GroupingInfo BuildGrouping(PurchasesSalesFilter filter)
    {
        var g1 = GetGroupSelect(filter.PrimaryGroup, "tp", "Level1");
        var g2 = GetGroupSelect(filter.SecondaryGroup, "tp1", "Level2");
        var g3 = GetGroupSelect(filter.ThirdGroup, "tp2", "Level3");

        bool hasAny = filter.PrimaryGroup != PsGroupBy.None
                   || filter.SecondaryGroup != PsGroupBy.None
                   || filter.ThirdGroup != PsGroupBy.None;

        var joins = new List<string>();
        var groupByCols = new List<string>();

        if (g1 != null) { joins.Add(g1.Join); groupByCols.AddRange(g1.GroupByCols); }
        if (g2 != null) { joins.Add(g2.Join); groupByCols.AddRange(g2.GroupByCols); }
        if (g3 != null) { joins.Add(g3.Join); groupByCols.AddRange(g3.GroupByCols); }

        return new GroupingInfo(
            g1?.Select ?? "NULL AS Level1, NULL AS Level1Value",
            g2?.Select ?? "NULL AS Level2, NULL AS Level2Value",
            g3?.Select ?? "NULL AS Level3, NULL AS Level3Value",
            string.Join("\n", joins),
            string.Join(", ", groupByCols),
            hasAny
        );
    }

    private record GroupFragment(string Select, string Join, List<string> GroupByCols);

    private GroupFragment? GetGroupSelect(PsGroupBy type, string alias, string levelName)
    {
        if (type == PsGroupBy.None) return null;

        var (code, value, join, groupByCols) = type switch
        {
            PsGroupBy.Category => (
                $"ISNULL({alias}.CategoryCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.CategoryCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.CategoryCode))+' - '+LTRIM(RTRIM({alias}.CategoryDescr)) END",
                $"LEFT JOIN tbl_ItemCategory {alias} ON it.fk_CategoryID = {alias}.pk_CategoryID",
                new List<string> { $"ISNULL({alias}.CategoryCode,'N/A')", $"CASE WHEN ISNULL({alias}.CategoryCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.CategoryCode))+' - '+LTRIM(RTRIM({alias}.CategoryDescr)) END" }
            ),
            PsGroupBy.Department => (
                $"ISNULL({alias}.DepartmentCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.DepartmentCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.DepartmentCode))+' - '+LTRIM(RTRIM({alias}.DepartmentDescr)) END",
                $"LEFT JOIN tbl_ItemDepartment {alias} ON it.fk_DepartmentID = {alias}.pk_DepartmentID",
                new List<string> { $"ISNULL({alias}.DepartmentCode,'N/A')", $"CASE WHEN ISNULL({alias}.DepartmentCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.DepartmentCode))+' - '+LTRIM(RTRIM({alias}.DepartmentDescr)) END" }
            ),
            PsGroupBy.Brand => (
                $"ISNULL({alias}.BrandCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.BrandCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.BrandCode))+' - '+LTRIM(RTRIM({alias}.BrandDesc)) END",
                $"LEFT JOIN tbl_Brands {alias} ON it.fk_BrandID = {alias}.pk_BrandID",
                new List<string> { $"ISNULL({alias}.BrandCode,'N/A')", $"CASE WHEN ISNULL({alias}.BrandCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.BrandCode))+' - '+LTRIM(RTRIM({alias}.BrandDesc)) END" }
            ),
            PsGroupBy.Season => (
                $"ISNULL({alias}.SeasonCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.SeasonCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.SeasonCode))+' - '+LTRIM(RTRIM({alias}.SeasonDesc)) END",
                $"LEFT JOIN tbl_Season {alias} ON it.fk_SeasonID = {alias}.pk_SeasonID",
                new List<string> { $"ISNULL({alias}.SeasonCode,'N/A')", $"CASE WHEN ISNULL({alias}.SeasonCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.SeasonCode))+' - '+LTRIM(RTRIM({alias}.SeasonDesc)) END" }
            ),
            PsGroupBy.Supplier => (
                $"ISNULL({alias}.pk_SupplierNo,'')",
                $"ISNULL(CASE WHEN {alias}.Company=1 THEN {alias}.LastCompanyName ELSE {alias}.FirstName+' '+{alias}.LastCompanyName END,'N/A')",
                $"LEFT JOIN tbl_Supplier {alias} ON s.fk_SupplierNo = {alias}.pk_SupplierNo",
                new List<string> { $"ISNULL({alias}.pk_SupplierNo,'')", $"ISNULL(CASE WHEN {alias}.Company=1 THEN {alias}.LastCompanyName ELSE {alias}.FirstName+' '+{alias}.LastCompanyName END,'N/A')" }
            ),
            PsGroupBy.Store => (
                $"ISNULL({alias}.pk_StoreCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.pk_StoreCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.pk_StoreCode))+' - '+LTRIM(RTRIM({alias}.StoreName)) END",
                $"LEFT JOIN tbl_Store {alias} ON tf.fk_StoreCode = {alias}.pk_StoreCode",
                new List<string> { $"ISNULL({alias}.pk_StoreCode,'N/A')", $"CASE WHEN ISNULL({alias}.pk_StoreCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.pk_StoreCode))+' - '+LTRIM(RTRIM({alias}.StoreName)) END" }
            ),
            PsGroupBy.Model => (
                $"ISNULL({alias}.ModelCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.ModelCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.ModelCode))+' - '+LTRIM(RTRIM({alias}.ModelNamePrimary)) END",
                $"LEFT JOIN tbl_Model {alias} ON it.fk_ModelID = {alias}.pk_ModelID",
                new List<string> { $"ISNULL({alias}.ModelCode,'N/A')", $"CASE WHEN ISNULL({alias}.ModelCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.ModelCode))+' - '+LTRIM(RTRIM({alias}.ModelNamePrimary)) END" }
            ),
            PsGroupBy.Colour => (
                $"ISNULL({alias}.ColourCode,'N/A')",
                $"CASE WHEN ISNULL({alias}.ColourCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.ColourCode))+' - '+LTRIM(RTRIM({alias}.ColourName)) END",
                $"LEFT JOIN tbl_Colour {alias} ON it.fk_ColourID = {alias}.pk_ColourID",
                new List<string> { $"ISNULL({alias}.ColourCode,'N/A')", $"CASE WHEN ISNULL({alias}.ColourCode,'N/A')='N/A' THEN 'N/A' ELSE LTRIM(RTRIM({alias}.ColourCode))+' - '+LTRIM(RTRIM({alias}.ColourName)) END" }
            ),
            PsGroupBy.Size => (
                $"ISNULL({alias}.SizeCode,'N/A')",
                $"ISNULL({alias}.SizeInvoiceDescr,'N/A')",
                $"LEFT JOIN tbl_Size {alias} ON it.fk_SizeID = {alias}.pk_SizeID",
                new List<string> { $"ISNULL({alias}.SizeCode,'N/A')", $"ISNULL({alias}.SizeInvoiceDescr,'N/A')" }
            ),
            PsGroupBy.GroupSize => (
                $"CAST(ISNULL(m.fk_SizeGroupID,0) AS NVARCHAR(20))",
                $"ISNULL({alias}.SizeGroupDesc,'N/A')",
                $"LEFT JOIN tbl_SizeGroup {alias} ON m.fk_SizeGroupID = {alias}.pk_SizeGroupID",
                new List<string> { $"CAST(ISNULL(m.fk_SizeGroupID,0) AS NVARCHAR(20))", $"ISNULL({alias}.SizeGroupDesc,'N/A')" }
            ),
            PsGroupBy.Fabric => (
                $"CAST(ISNULL(m.fk_FabricID,0) AS NVARCHAR(20))",
                $"ISNULL({alias}.FabricDesc,'N/A')",
                $"LEFT JOIN tbl_Fabric {alias} ON m.fk_FabricID = {alias}.pk_FabricID",
                new List<string> { $"CAST(ISNULL(m.fk_FabricID,0) AS NVARCHAR(20))", $"ISNULL({alias}.FabricDesc,'N/A')" }
            ),
            _ => ("''", "''", "", new List<string>())
        };

        return new GroupFragment(
            $"{code} AS {levelName}, {value} AS {levelName}Value",
            join,
            groupByCols
        );
    }

    #endregion

    #region Item Filters

    private record ItemFilterInfo(string WhereClause, List<SqlParameter> Parameters);

    private ItemFilterInfo BuildItemFilters(PurchasesSalesFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();
        int idx = 0;

        if (filter.HasStoreFilter)
        {
            var names = new List<string>();
            foreach (var s in filter.StoreCodes)
            {
                var p = $"@st{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, s));
            }
            sb.Append($" AND t3.fk_StoreCode IN ({string.Join(",", names)})");
        }

        if (filter.HasItemFilter)
        {
            var names = new List<string>();
            foreach (var id in filter.ItemIds)
            {
                var p = $"@it{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, id));
            }
            sb.Append($" AND t1.fk_ItemID IN ({string.Join(",", names)})");
        }

        if (filter.HasCategoryFilter)
        {
            var names = new List<string>();
            foreach (var c in filter.CategoryIds)
            {
                var p = $"@cat{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, c));
            }
            sb.Append($" AND t2.fk_CategoryID IN ({string.Join(",", names)})");
        }

        if (filter.HasDepartmentFilter)
        {
            var names = new List<string>();
            foreach (var d in filter.DepartmentIds)
            {
                var p = $"@dep{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, d));
            }
            sb.Append($" AND t2.fk_DepartmentID IN ({string.Join(",", names)})");
        }

        if (filter.HasSupplierFilter)
        {
            var names = new List<string>();
            foreach (var s in filter.SupplierIds)
            {
                var p = $"@sup{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, s));
            }
            sb.Append($" AND t4.fk_SupplierNo IN ({string.Join(",", names)})");
        }

        if (filter.HasBrandFilter)
        {
            var names = new List<string>();
            foreach (var b in filter.BrandIds)
            {
                var p = $"@br{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, b));
            }
            sb.Append($" AND t2.fk_BrandID IN ({string.Join(",", names)})");
        }

        if (filter.HasSeasonFilter)
        {
            var names = new List<string>();
            foreach (var s in filter.SeasonIds)
            {
                var p = $"@sea{idx++}";
                names.Add(p);
                parms.Add(new SqlParameter(p, s));
            }
            sb.Append($" AND t2.fk_SeasonID IN ({string.Join(",", names)})");
        }

        if (filter.ItemsSelection != null)
        {
            var psDimCols = new DimensionFilterBuilder.ColumnMap(
                Category: "t2.fk_CategoryID",
                Department: "t2.fk_DepartmentID",
                Brand: "t2.fk_BrandID",
                Season: "t2.fk_SeasonID",
                Item: "t1.fk_ItemID",
                Store: "t3.fk_StoreCode",
                Supplier: "t4.fk_SupplierNo");
            var (dimWhere, dimParms) = DimensionFilterBuilder.Build(filter.ItemsSelection, psDimCols, idx);
            sb.Append(dimWhere);
            parms.AddRange(dimParms);
        }

        return new ItemFilterInfo(sb.ToString(), parms);
    }

    #endregion

    #region Column Filters & Sorting

    private static readonly Dictionary<string, string> ColumnSqlMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ItemCode"] = "d.ItemCode",
        ["ItemName"] = "d.ItemNamePrimary",
        ["Level1"] = "d.Level1",
        ["Level1Value"] = "d.Level1Value",
        ["Level2"] = "d.Level2",
        ["Level2Value"] = "d.Level2Value",
        ["Level3"] = "d.Level3",
        ["Level3Value"] = "d.Level3Value",
        ["QuantityPurchased"] = "d.QuantityPurchased",
        ["NetPurchasedValue"] = "d.NetPurchasedValue",
        ["GrossPurchasedValue"] = "d.GrossPurchasedValue",
        ["QuantitySold"] = "d.QuantitySold",
        ["NetSoldValue"] = "d.NetSoldValue",
        ["GrossSoldValue"] = "d.GrossSoldValue",
        ["Profit"] = "d.Profit",
        ["TotalStockQty"] = "d.TotalStockQty"
    };

    private static readonly HashSet<string> TextColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ItemCode", "ItemName", "Level1", "Level1Value", "Level2", "Level2Value", "Level3", "Level3Value"
    };

    private string ResolveSortExpression(PurchasesSalesFilter filter)
    {
        var col = filter.SortColumn ?? "ItemCode";
        var dir = string.Equals(filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        if (!ColumnSqlMap.TryGetValue(col, out var sqlExpr))
            sqlExpr = "d.ItemCode";

        var parts = new List<string>();

        if (filter.PrimaryGroup != PsGroupBy.None)
        {
            parts.Add("d.Level1Value");
            parts.Add("d.Level1");
        }
        if (filter.SecondaryGroup != PsGroupBy.None)
        {
            parts.Add("d.Level2Value");
            parts.Add("d.Level2");
        }
        if (filter.ThirdGroup != PsGroupBy.None)
        {
            parts.Add("d.Level3Value");
            parts.Add("d.Level3");
        }

        parts.Add($"{sqlExpr} {dir}");

        return string.Join(", ", parts);
    }

    private (string whereClause, List<SqlParameter> filterParams) BuildColumnFilterClause(PurchasesSalesFilter filter)
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
                default:
                {
                    var sqlOp = op switch
                    {
                        "neq" => "<>", "lt" => "<", "lte" => "<=", "gt" => ">", "gte" => ">=", _ => "="
                    };
                    if (isText)
                    {
                        conditions.Add($"{sqlExpr} {sqlOp} {paramName}");
                        parameters.Add(new SqlParameter(paramName, value));
                    }
                    else if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
                    {
                        conditions.Add($"{sqlExpr} {sqlOp} {paramName}");
                        parameters.Add(new SqlParameter(paramName, numVal));
                    }
                    break;
                }
            }
        }

        if (!conditions.Any())
            return ("", new List<SqlParameter>());

        return ($"\nWHERE {string.Join(" AND ", conditions)}", parameters);
    }

    #endregion

    #region Query Execution

    private List<SqlParameter> BuildCommonParameters(PurchasesSalesFilter filter, ItemFilterInfo itemFilters)
    {
        var parms = new List<SqlParameter>
        {
            new("@DateFrom", filter.DateFrom.Date),
            new("@DateTo", filter.DateTo.Date)
        };
        parms.AddRange(itemFilters.Parameters);
        return parms;
    }

    private static SqlParameter Clone(SqlParameter src) =>
        new(src.ParameterName, src.Value);

    private async Task<int> ExecuteCountAsync(SqlConnection conn, string sql, List<SqlParameter> parameters, PurchasesSalesFilter filter)
    {
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parameters) cmd.Parameters.Add(Clone(p));
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<List<PurchasesSalesRow>> ExecuteDataAsync(
        SqlConnection conn, string sql, List<SqlParameter> parameters,
        PurchasesSalesFilter filter, GroupingInfo grouping)
    {
        var items = new List<PurchasesSalesRow>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parameters) cmd.Parameters.Add(Clone(p));
        cmd.Parameters.AddWithValue("@Skip", filter.Skip);
        cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new PurchasesSalesRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i)) continue;
                var name = reader.GetName(i);
                switch (name)
                {
                    case "Level1": row.Level1 = reader.GetString(i); break;
                    case "Level1Value": row.Level1Value = reader.GetString(i); break;
                    case "Level2": row.Level2 = reader.GetString(i); break;
                    case "Level2Value": row.Level2Value = reader.GetString(i); break;
                    case "Level3": row.Level3 = reader.GetString(i); break;
                    case "Level3Value": row.Level3Value = reader.GetString(i); break;
                    case "ItemCode": row.ItemCode = reader.GetString(i); break;
                    case "ItemNamePrimary": row.ItemName = reader.GetString(i); break;
                    case "QuantityPurchased": row.QuantityPurchased = reader.GetDecimal(i); break;
                    case "NetPurchasedValue": row.NetPurchasedValue = reader.GetDecimal(i); break;
                    case "GrossPurchasedValue": row.GrossPurchasedValue = reader.GetDecimal(i); break;
                    case "QuantitySold": row.QuantitySold = reader.GetDecimal(i); break;
                    case "NetSoldValue": row.NetSoldValue = reader.GetDecimal(i); break;
                    case "GrossSoldValue": row.GrossSoldValue = reader.GetDecimal(i); break;
                    case "Profit": row.Profit = reader.GetDecimal(i); break;
                    case "TotalStockQty": row.TotalStockQty = reader.GetDecimal(i); break;
                }
            }
            items.Add(row);
        }
        return items;
    }

    private async Task<PurchasesSalesTotals> ExecuteTotalsAsync(
        SqlConnection conn, string sql, PurchasesSalesFilter filter, ItemFilterInfo itemFilters)
    {
        var totals = new PurchasesSalesTotals();
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        cmd.Parameters.AddWithValue("@DateFrom", filter.DateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", filter.DateTo.Date);
        foreach (var p in itemFilters.Parameters) cmd.Parameters.Add(Clone(p));

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totals.TotalQtyPurchased = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
            totals.TotalNetPurchased = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
            totals.TotalGrossPurchased = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
            totals.TotalQtySold = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
            totals.TotalNetSold = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
            totals.TotalGrossSold = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
            totals.TotalStockQty = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
        }
        return totals;
    }

    #endregion

    #region Drill-Down: Transaction Details

    public async Task<List<TransactionDetailRow>> GetTransactionDetailsAsync(
        string itemCode, string transactionType, DateTime dateFrom, DateTime dateTo, List<string>? storeCodes = null)
    {
        var sql = BuildTransactionDetailSql(transactionType, storeCodes);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 60;
        cmd.Parameters.AddWithValue("@ItemCode", itemCode);
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Date);
        cmd.Parameters.AddWithValue("@DateTo", dateTo.Date);

        if (storeCodes?.Any() == true)
        {
            for (int i = 0; i < storeCodes.Count; i++)
                cmd.Parameters.AddWithValue($"@SC{i}", storeCodes[i]);
        }

        var results = new List<TransactionDetailRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TransactionDetailRow
            {
                DateTrans = reader.GetDateTime(reader.GetOrdinal("DateTrans")),
                Kind = reader.IsDBNull(reader.GetOrdinal("Kind")) ? "" : reader.GetString(reader.GetOrdinal("Kind")),
                DocumentNumber = reader.IsDBNull(reader.GetOrdinal("DocumentNumber")) ? "" : reader.GetString(reader.GetOrdinal("DocumentNumber")),
                EntityCode = reader.IsDBNull(reader.GetOrdinal("EntityCode")) ? "" : reader.GetString(reader.GetOrdinal("EntityCode")),
                EntityName = reader.IsDBNull(reader.GetOrdinal("EntityName")) ? "" : reader.GetString(reader.GetOrdinal("EntityName")),
                StoreCode = reader.IsDBNull(reader.GetOrdinal("StoreCode")) ? "" : reader.GetString(reader.GetOrdinal("StoreCode")),
                ItemCode = reader.IsDBNull(reader.GetOrdinal("ItemCode")) ? "" : reader.GetString(reader.GetOrdinal("ItemCode")),
                ItemName = reader.IsDBNull(reader.GetOrdinal("ItemName")) ? "" : reader.GetString(reader.GetOrdinal("ItemName")),
                Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity")),
                UnitPrice = reader.GetDecimal(reader.GetOrdinal("UnitPrice")),
                Discount = reader.GetDecimal(reader.GetOrdinal("Discount")),
                NetAmount = reader.GetDecimal(reader.GetOrdinal("NetAmount")),
                VatAmount = reader.GetDecimal(reader.GetOrdinal("VatAmount")),
                GrossAmount = reader.GetDecimal(reader.GetOrdinal("GrossAmount"))
            });
        }

        foreach (var r in results)
            r.KindDescription = TransactionDetailRow.GetKindDescription(r.Kind);

        return results;
    }

    private static string BuildTransactionDetailSql(string transactionType, List<string>? storeCodes)
    {
        var storeIn = "";
        if (storeCodes?.Any() == true)
        {
            var paramNames = string.Join(",", storeCodes.Select((_, i) => $"@SC{i}"));
            storeIn = $" AND h.fk_StoreCode IN ({paramNames})";
        }

        var parts = new List<string>();

        if (transactionType is "purchases" or "all")
        {
            parts.Add($@"
SELECT CONVERT(DATE, h.DateTrans) AS DateTrans, 'P' AS Kind,
       ISNULL(h.PurchInvoiceNumber,'') AS DocumentNumber,
       ISNULL(sup.pk_SupplierNo,'') AS EntityCode,
       CASE WHEN ISNULL(sup.Company,0) = 1 THEN ISNULL(sup.LastCompanyName,'') ELSE ISNULL(sup.FirstName,'') + ' ' + ISNULL(sup.LastCompanyName,'') END AS EntityName,
       ISNULL(h.fk_StoreCode,'') AS StoreCode,
       it.ItemCode, it.ItemNamePrimary AS ItemName,
       d.Quantity, ISNULL(d.ItemCost,0) AS UnitPrice,
       (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) AS Discount,
       (d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0))) AS NetAmount,
       ISNULL(d.VatAmount,0) AS VatAmount,
       (d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) + ISNULL(d.VatAmount,0)) AS GrossAmount
FROM tbl_PurchInvoiceDetails d
INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
INNER JOIN tbl_PurchInvoiceHeader h ON d.fk_PurchInvoiceID = h.pk_PurchInvoiceID
LEFT JOIN tbl_Supplier sup ON h.fk_SupplierCode = sup.pk_SupplierNo
WHERE it.ItemCode = @ItemCode
  AND CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo{storeIn}");

            parts.Add($@"
SELECT CONVERT(DATE, h.DateTrans) AS DateTrans, 'E' AS Kind,
       ISNULL(h.InvoiceNumber,'') AS DocumentNumber,
       ISNULL(sup.pk_SupplierNo,'') AS EntityCode,
       CASE WHEN ISNULL(sup.Company,0) = 1 THEN ISNULL(sup.LastCompanyName,'') ELSE ISNULL(sup.FirstName,'') + ' ' + ISNULL(sup.LastCompanyName,'') END AS EntityName,
       ISNULL(h.fk_StoreCode,'') AS StoreCode,
       it.ItemCode, it.ItemNamePrimary AS ItemName,
       (d.Quantity * -1) AS Quantity, ISNULL(d.ItemCost,0) AS UnitPrice,
       (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) AS Discount,
       ((d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0))) * -1) AS NetAmount,
       (ISNULL(d.VatAmount,0) * -1) AS VatAmount,
       ((d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) + ISNULL(d.VatAmount,0)) * -1) AS GrossAmount
FROM tbl_PurchReturnDetails d
INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
INNER JOIN tbl_PurchReturnHeader h ON d.fk_PurchReturnID = h.pk_PurchReturnID
LEFT JOIN tbl_Supplier sup ON h.fk_SupplierCode = sup.pk_SupplierNo
WHERE it.ItemCode = @ItemCode
  AND CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo{storeIn}");
        }

        if (transactionType is "sales" or "all")
        {
            parts.Add($@"
SELECT CONVERT(DATE, h.DateTrans) AS DateTrans, 'I' AS Kind,
       ISNULL(h.pk_InvoiceID,'') AS DocumentNumber,
       ISNULL(c.pk_CustomerNo,'') AS EntityCode,
       CASE WHEN ISNULL(c.Company,0) = 1 THEN ISNULL(c.LastCompanyName,'') ELSE ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastCompanyName,'') END AS EntityName,
       ISNULL(h.fk_StoreCode,'') AS StoreCode,
       it.ItemCode, it.ItemNamePrimary AS ItemName,
       d.Quantity, ISNULL(d.ItemPriceExcl,0) AS UnitPrice,
       (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) AS Discount,
       (d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0))) AS NetAmount,
       ISNULL(d.VatAmount,0) AS VatAmount,
       (d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) + ISNULL(d.VatAmount,0)) AS GrossAmount
FROM tbl_InvoiceDetails d
INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
INNER JOIN tbl_InvoiceHeader h ON d.fk_Invoice = h.pk_InvoiceID
LEFT JOIN tbl_Customer c ON h.fk_CustomerCode = c.pk_CustomerNo
WHERE it.ItemCode = @ItemCode
  AND CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo{storeIn}");

            parts.Add($@"
SELECT CONVERT(DATE, h.DateTrans) AS DateTrans, 'C' AS Kind,
       ISNULL(h.pk_CreditID,'') AS DocumentNumber,
       ISNULL(c.pk_CustomerNo,'') AS EntityCode,
       CASE WHEN ISNULL(c.Company,0) = 1 THEN ISNULL(c.LastCompanyName,'') ELSE ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastCompanyName,'') END AS EntityName,
       ISNULL(h.fk_StoreCode,'') AS StoreCode,
       it.ItemCode, it.ItemNamePrimary AS ItemName,
       (d.Quantity * -1) AS Quantity, ISNULL(d.ItemPriceExcl,0) AS UnitPrice,
       (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) AS Discount,
       ((d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0))) * -1) AS NetAmount,
       (ISNULL(d.VatAmount,0) * -1) AS VatAmount,
       ((d.Amount - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) + ISNULL(d.VatAmount,0)) * -1) AS GrossAmount
FROM tbl_CreditDetails d
INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
INNER JOIN tbl_CreditHeader h ON d.fk_Credit = h.pk_CreditID
LEFT JOIN tbl_Customer c ON h.fk_CustomerCode = c.pk_CustomerNo
WHERE it.ItemCode = @ItemCode
  AND CONVERT(DATE, h.DateTrans) BETWEEN @DateFrom AND @DateTo{storeIn}");
        }

        return string.Join("\nUNION ALL\n", parts) + "\nORDER BY DateTrans DESC, DocumentNumber";
    }

    #endregion

    #region Document Detail

    public async Task<DocumentDetailResult?> GetDocumentDetailAsync(string docType, string documentNumber)
    {
        var (headerSql, detailSql) = BuildDocumentDetailSql(docType);
        if (headerSql == null) return null;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var result = new DocumentDetailResult
        {
            DocType = docType,
            DocTypeDescription = DocumentDetailResult.GetDocTypeDescription(docType),
            DocumentNumber = documentNumber
        };

        using (var cmd = new SqlCommand(headerSql, conn))
        {
            cmd.CommandTimeout = 30;
            cmd.Parameters.AddWithValue("@DocNumber", documentNumber);
            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            result.DocumentDate = reader.GetDateTime(reader.GetOrdinal("DocumentDate"));
            result.EntityCode = reader.IsDBNull(reader.GetOrdinal("EntityCode")) ? "" : reader.GetString(reader.GetOrdinal("EntityCode"));
            result.EntityName = reader.IsDBNull(reader.GetOrdinal("EntityName")) ? "" : reader.GetString(reader.GetOrdinal("EntityName"));
            result.StoreCode = reader.IsDBNull(reader.GetOrdinal("StoreCode")) ? "" : reader.GetString(reader.GetOrdinal("StoreCode"));
        }

        using (var cmd = new SqlCommand(detailSql, conn))
        {
            cmd.CommandTimeout = 30;
            cmd.Parameters.AddWithValue("@DocNumber", documentNumber);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var line = new DocumentLineItem
                {
                    ItemCode = reader.IsDBNull(reader.GetOrdinal("ItemCode")) ? "" : reader.GetString(reader.GetOrdinal("ItemCode")),
                    ItemName = reader.IsDBNull(reader.GetOrdinal("ItemName")) ? "" : reader.GetString(reader.GetOrdinal("ItemName")),
                    Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity")),
                    UnitPrice = reader.GetDecimal(reader.GetOrdinal("UnitPrice")),
                    Discount = reader.GetDecimal(reader.GetOrdinal("Discount")),
                    NetAmount = reader.GetDecimal(reader.GetOrdinal("NetAmount")),
                    VatAmount = reader.GetDecimal(reader.GetOrdinal("VatAmount")),
                    GrossAmount = reader.GetDecimal(reader.GetOrdinal("GrossAmount"))
                };
                result.Lines.Add(line);
            }
        }

        result.TotalNet = result.Lines.Sum(l => l.NetAmount);
        result.TotalVat = result.Lines.Sum(l => l.VatAmount);
        result.TotalGross = result.Lines.Sum(l => l.GrossAmount);

        return result;
    }

    private static (string? headerSql, string? detailSql) BuildDocumentDetailSql(string docType)
    {
        return docType switch
        {
            "P" => (
                @"SELECT TOP 1 CONVERT(DATE, h.DateTrans) AS DocumentDate,
                         h.fk_StoreCode AS StoreCode,
                         ISNULL(sup.pk_SupplierNo,'') AS EntityCode,
                         CASE WHEN ISNULL(sup.Company,0)=1 THEN ISNULL(sup.LastCompanyName,'')
                              ELSE ISNULL(sup.FirstName,'')+' '+ISNULL(sup.LastCompanyName,'') END AS EntityName
                  FROM tbl_PurchInvoiceHeader h
                  LEFT JOIN tbl_Supplier sup ON h.fk_SupplierCode = sup.pk_SupplierNo
                  WHERE h.PurchInvoiceNumber = @DocNumber",
                @"SELECT it.ItemCode, it.ItemNamePrimary AS ItemName,
                         d.Quantity,
                         ISNULL(d.ItemCost,0) AS UnitPrice,
                         (ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0)) AS Discount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))) AS NetAmount,
                         ISNULL(d.VatAmount,0) AS VatAmount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))+ISNULL(d.VatAmount,0)) AS GrossAmount
                  FROM tbl_PurchInvoiceDetails d
                  INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
                  INNER JOIN tbl_PurchInvoiceHeader h ON d.fk_PurchInvoiceID = h.pk_PurchInvoiceID
                  WHERE h.PurchInvoiceNumber = @DocNumber
                  ORDER BY it.ItemCode"),

            "E" => (
                @"SELECT TOP 1 CONVERT(DATE, h.DateTrans) AS DocumentDate,
                         h.fk_StoreCode AS StoreCode,
                         ISNULL(sup.pk_SupplierNo,'') AS EntityCode,
                         CASE WHEN ISNULL(sup.Company,0)=1 THEN ISNULL(sup.LastCompanyName,'')
                              ELSE ISNULL(sup.FirstName,'')+' '+ISNULL(sup.LastCompanyName,'') END AS EntityName
                  FROM tbl_PurchReturnHeader h
                  LEFT JOIN tbl_Supplier sup ON h.fk_SupplierCode = sup.pk_SupplierNo
                  WHERE h.InvoiceNumber = @DocNumber",
                @"SELECT it.ItemCode, it.ItemNamePrimary AS ItemName,
                         d.Quantity,
                         ISNULL(d.ItemCost,0) AS UnitPrice,
                         (ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0)) AS Discount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))) AS NetAmount,
                         ISNULL(d.VatAmount,0) AS VatAmount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))+ISNULL(d.VatAmount,0)) AS GrossAmount
                  FROM tbl_PurchReturnDetails d
                  INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
                  INNER JOIN tbl_PurchReturnHeader h ON d.fk_PurchReturnID = h.pk_PurchReturnID
                  WHERE h.InvoiceNumber = @DocNumber
                  ORDER BY it.ItemCode"),

            "I" => (
                @"SELECT TOP 1 CONVERT(DATE, h.DateTrans) AS DocumentDate,
                         h.fk_StoreCode AS StoreCode,
                         ISNULL(c.pk_CustomerNo,'') AS EntityCode,
                         CASE WHEN ISNULL(c.Company,0)=1 THEN ISNULL(c.LastCompanyName,'')
                              ELSE ISNULL(c.FirstName,'')+' '+ISNULL(c.LastCompanyName,'') END AS EntityName
                  FROM tbl_InvoiceHeader h
                  LEFT JOIN tbl_Customer c ON h.fk_CustomerCode = c.pk_CustomerNo
                  WHERE h.pk_InvoiceID = @DocNumber",
                @"SELECT it.ItemCode, it.ItemNamePrimary AS ItemName,
                         d.Quantity,
                         ISNULL(d.ItemPriceExcl,0) AS UnitPrice,
                         (ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0)) AS Discount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))) AS NetAmount,
                         ISNULL(d.VatAmount,0) AS VatAmount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))+ISNULL(d.VatAmount,0)) AS GrossAmount
                  FROM tbl_InvoiceDetails d
                  INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
                  INNER JOIN tbl_InvoiceHeader h ON d.fk_Invoice = h.pk_InvoiceID
                  WHERE h.pk_InvoiceID = @DocNumber
                  ORDER BY it.ItemCode"),

            "C" => (
                @"SELECT TOP 1 CONVERT(DATE, h.DateTrans) AS DocumentDate,
                         h.fk_StoreCode AS StoreCode,
                         ISNULL(c.pk_CustomerNo,'') AS EntityCode,
                         CASE WHEN ISNULL(c.Company,0)=1 THEN ISNULL(c.LastCompanyName,'')
                              ELSE ISNULL(c.FirstName,'')+' '+ISNULL(c.LastCompanyName,'') END AS EntityName
                  FROM tbl_CreditHeader h
                  LEFT JOIN tbl_Customer c ON h.fk_CustomerCode = c.pk_CustomerNo
                  WHERE h.pk_CreditID = @DocNumber",
                @"SELECT it.ItemCode, it.ItemNamePrimary AS ItemName,
                         d.Quantity,
                         ISNULL(d.ItemPriceExcl,0) AS UnitPrice,
                         (ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0)) AS Discount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))) AS NetAmount,
                         ISNULL(d.VatAmount,0) AS VatAmount,
                         (d.Amount-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))+ISNULL(d.VatAmount,0)) AS GrossAmount
                  FROM tbl_CreditDetails d
                  INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID
                  INNER JOIN tbl_CreditHeader h ON d.fk_Credit = h.pk_CreditID
                  WHERE h.pk_CreditID = @DocNumber
                  ORDER BY it.ItemCode"),

            _ => (null, null)
        };
    }

    #endregion
}
