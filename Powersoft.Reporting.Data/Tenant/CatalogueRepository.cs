using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class CatalogueRepository : ICatalogueRepository
{
    private readonly string _connectionString;

    public CatalogueRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PagedResult<CatalogueRow>> GetCatalogueDataAsync(CatalogueFilter filter)
    {
        var grouping = BuildGrouping(filter);
        var itemFilters = BuildItemFilters(filter);
        var isSummary = filter.IsSummary;

        var innerUnion = BuildUnionAll(filter, grouping, itemFilters);
        var outerSql = BuildOuterQuery(filter, grouping, innerUnion, isSummary);

        var (colFilterWhere, colFilterParams) = BuildColumnFilterClause(filter);

        var dataSql = $@";WITH Data AS ({outerSql})
SELECT * FROM Data d{colFilterWhere}
ORDER BY {ResolveSortExpression(filter)}
OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@";WITH Data AS ({outerSql})
SELECT COUNT(*) FROM Data d{colFilterWhere}";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var allParams = BuildCommonParameters(filter, itemFilters);
        allParams.AddRange(colFilterParams);

        int totalCount = await ExecuteCountAsync(conn, countSql, allParams);
        var items = await ExecuteDataAsync(conn, dataSql, allParams, filter, isSummary);

        return new PagedResult<CatalogueRow>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<CatalogueTotals> GetCatalogueTotalsAsync(CatalogueFilter filter)
    {
        var itemFilters = BuildItemFilters(filter);
        var totalsSql = BuildTotalsQuery(filter, itemFilters);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var parms = BuildCommonParameters(filter, itemFilters);
        using var cmd = new SqlCommand(totalsSql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parms) cmd.Parameters.Add(Clone(p));

        var totals = new CatalogueTotals();
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totals.TotalQuantity = GetDecimalSafe(reader, "TotalQuantity");
            totals.TotalValueBeforeDiscount = GetDecimalSafe(reader, "TotalValueBeforeDiscount");
            totals.TotalDiscount = GetDecimalSafe(reader, "TotalDiscount");
            totals.TotalNetValue = GetDecimalSafe(reader, "TotalNetValue");
            totals.TotalVatAmount = GetDecimalSafe(reader, "TotalVatAmount");
            totals.TotalGrossAmount = GetDecimalSafe(reader, "TotalGrossAmount");
            totals.TotalTransactionCost = GetDecimalSafe(reader, "TotalTransactionCost");
            totals.TotalTotalCost = GetDecimalSafe(reader, "TotalTotalCost");
            totals.TotalProfitValue = GetDecimalSafe(reader, "TotalProfitValue");
            totals.TotalStockQty = GetDecimalSafe(reader, "TotalStockQty");
            totals.TotalStockValue = GetDecimalSafe(reader, "TotalStockValue");
        }
        return totals;
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

    /// <summary>
    /// Per-store stock breakdown for a single item.
    /// SQL adapted from original Powersoft365 (CloudQueries WQR.LoadStockPerStoreForItemWithShelf, line 118646)
    /// joined with tbl_Store to add StoreName.
    /// </summary>
    public async Task<ItemStockPositionResult> GetItemStockPositionAsync(string itemCode)
    {
        const string sql = @"
SELECT
    s.pk_StoreCode                              AS StoreCode,
    ISNULL(s.StoreName, '')                     AS StoreName,
    ISNULL(t1.Stock, 0)                         AS OnStock,
    ISNULL(t1.StockOnTransfer, 0)               AS OnTransfer,
    ISNULL(t1.StockReserved, 0)                 AS Reserved,
    ISNULL(t1.StockOrdered, 0)                  AS Ordered,
    ISNULL(t1.StockOnWB, 0)                     AS OnWaybill,
    ISNULL(t3.ShelfDescr, '')                   AS Shelf,
    ISNULL(t1.MinimumStock, 0)                  AS MinimumStock,
    ISNULL(t1.RequiredStock, 0)                 AS RequiredStock,
    ISNULL(it.ItemCode, @ItemCode)              AS ItemCode,
    ISNULL(it.ItemNamePrimary, '')              AS ItemName
FROM tbl_Store s
LEFT JOIN tbl_Item it
    ON it.ItemCode = @ItemCode
LEFT JOIN tbl_RelItemStore t1
    ON t1.fk_ItemID = it.pk_ItemID
   AND t1.fk_StoreCode = s.pk_StoreCode
LEFT JOIN tbl_Shelf t3
    ON t1.fk_Shelf = t3.tk_Shelf
ORDER BY s.pk_StoreCode;";

        var result = new ItemStockPositionResult { ItemCode = itemCode };

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@ItemCode", System.Data.SqlDbType.NVarChar, 50) { Value = itemCode });

        using var reader = await cmd.ExecuteReaderAsync();
        bool nameCaptured = false;
        while (await reader.ReadAsync())
        {
            if (!nameCaptured)
            {
                result.ItemName = reader["ItemName"] as string ?? string.Empty;
                nameCaptured = true;
            }
            result.Rows.Add(new ItemStockPositionRow
            {
                StoreCode = reader["StoreCode"] as string ?? string.Empty,
                StoreName = reader["StoreName"] as string ?? string.Empty,
                OnStock = Convert.ToDecimal(reader["OnStock"]),
                OnTransfer = Convert.ToDecimal(reader["OnTransfer"]),
                Reserved = Convert.ToDecimal(reader["Reserved"]),
                Ordered = Convert.ToDecimal(reader["Ordered"]),
                OnWaybill = Convert.ToDecimal(reader["OnWaybill"]),
                Shelf = reader["Shelf"] as string ?? string.Empty,
                MinimumStock = Convert.ToDecimal(reader["MinimumStock"]),
                RequiredStock = Convert.ToDecimal(reader["RequiredStock"])
            });
        }

        return result;
    }

    #region SQL Generation

    /// <summary>
    /// Builds the WHERE clause for the date range filter.
    /// Honours filter.DateBasis (TransactionDate → h.DateTrans, SessionDate → h.SessionDateTime)
    /// and filter.UseDateTime (false → CONVERT(DATE, ...), true → CONVERT(DATETIME, ...)).
    /// Mirrors original repPowerReportCatalogue.aspx.vb:3642-3654.
    /// </summary>
    private static string BuildDateWhere(CatalogueFilter filter)
    {
        var column = filter.DateBasis == CatalogueDateBasis.SessionDate
            ? "h.SessionDateTime"
            : "h.DateTrans";
        var conv = filter.UseDateTime ? "DATETIME" : "DATE";
        return $"WHERE CONVERT({conv}, {column}) BETWEEN CONVERT({conv}, @DateFrom) AND CONVERT({conv}, @DateTo)";
    }

    private string BuildUnionAll(CatalogueFilter filter, GroupingInfo grouping, ItemFilterInfo itemFilters)
    {
        var sb = new StringBuilder();
        var dateWhere = BuildDateWhere(filter);
        var filterCond = itemFilters.WhereClause;
        var saleOnlyCond = itemFilters.SaleOnlyWhereClause;
        bool isSummary = filter.IsSummary;

        if (filter.ReportOn == CatalogueReportOn.Sale || filter.ReportOn == CatalogueReportOn.Both)
        {
            AppendTransactionLeg(sb, grouping, dateWhere, filterCond, isSummary, filter,
                detailTable: "tbl_InvoiceDetails",
                headerTable: "tbl_InvoiceHeader",
                headerJoin: "d.fk_Invoice = h.pk_InvoiceID",
                entityTable: "tbl_Customer",
                entityJoin: "h.fk_CustomerCode = e.pk_CustomerNo",
                entityCodeExpr: "ISNULL(e.pk_CustomerNo,'')",
                entityNameExpr: "CASE WHEN ISNULL(e.Company,0) = 1 THEN ISNULL(e.LastCompanyName,'') ELSE ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastCompanyName,'') END",
                invoiceIdExpr: "h.pk_InvoiceID",
                invoiceType: "I",
                signMultiplier: 1,
                saleOnlyCond: saleOnlyCond);

            sb.AppendLine("UNION ALL");

            AppendTransactionLeg(sb, grouping, dateWhere, filterCond, isSummary, filter,
                detailTable: "tbl_CreditDetails",
                headerTable: "tbl_CreditHeader",
                headerJoin: "d.fk_Credit = h.pk_CreditID",
                entityTable: "tbl_Customer",
                entityJoin: "h.fk_CustomerCode = e.pk_CustomerNo",
                entityCodeExpr: "ISNULL(e.pk_CustomerNo,'')",
                entityNameExpr: "CASE WHEN ISNULL(e.Company,0) = 1 THEN ISNULL(e.LastCompanyName,'') ELSE ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastCompanyName,'') END",
                invoiceIdExpr: "h.pk_CreditID",
                invoiceType: "C",
                signMultiplier: -1,
                saleOnlyCond: saleOnlyCond);
        }

        if (filter.ReportOn == CatalogueReportOn.Purchase || filter.ReportOn == CatalogueReportOn.Both)
        {
            if (sb.Length > 0) sb.AppendLine("UNION ALL");

            // Note: saleOnlyCond intentionally NOT appended — purchase headers/entities don't
            // have fk_CustomerCode, fk_AgentID, PostalCode columns. This matches original
            // repPowerReportCatalogue.aspx.vb behaviour where customer/agent/postalcode
            // selections simply never applied to purchase legs.
            AppendTransactionLeg(sb, grouping, dateWhere, filterCond, isSummary, filter,
                detailTable: "tbl_PurchInvoiceDetails",
                headerTable: "tbl_PurchInvoiceHeader",
                headerJoin: "d.fk_PurchInvoiceID = h.pk_PurchInvoiceID",
                entityTable: "tbl_Supplier",
                entityJoin: "h.fk_SupplierCode = e.pk_SupplierNo",
                entityCodeExpr: "ISNULL(e.pk_SupplierNo,'')",
                entityNameExpr: "CASE WHEN ISNULL(e.Company,0) = 1 THEN ISNULL(e.LastCompanyName,'') ELSE ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastCompanyName,'') END",
                invoiceIdExpr: "h.pk_PurchInvoiceID",
                invoiceType: "P",
                signMultiplier: 1);

            sb.AppendLine("UNION ALL");

            AppendTransactionLeg(sb, grouping, dateWhere, filterCond, isSummary, filter,
                detailTable: "tbl_PurchReturnDetails",
                headerTable: "tbl_PurchReturnHeader",
                headerJoin: "d.fk_PurchReturnID = h.pk_PurchReturnID",
                entityTable: "tbl_Supplier",
                entityJoin: "h.fk_SupplierCode = e.pk_SupplierNo",
                entityCodeExpr: "ISNULL(e.pk_SupplierNo,'')",
                entityNameExpr: "CASE WHEN ISNULL(e.Company,0) = 1 THEN ISNULL(e.LastCompanyName,'') ELSE ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastCompanyName,'') END",
                invoiceIdExpr: "h.pk_PurchReturnID",
                invoiceType: "E",
                signMultiplier: -1);
        }

        return sb.ToString();
    }

    private static string ResolveCostExpr(CatalogueCostBasis costType) => costType switch
    {
        CatalogueCostBasis.AverageCost => "ISNULL(it.AverageCost,0)",
        CatalogueCostBasis.WeightedAverageCost => "ISNULL(it.WeightedAverageCost,0)",
        CatalogueCostBasis.CostOnSale => "ISNULL(d.ItemCost,0)",
        CatalogueCostBasis.Price1 => "ISNULL(it.Price1Excl,0)",
        CatalogueCostBasis.Price2 => "ISNULL(it.Price2Excl,0)",
        CatalogueCostBasis.Price3 => "ISNULL(it.Price3Excl,0)",
        _ => "ISNULL(it.Cost,0)"
    };

    /// <summary>
    /// Builds the dynamic CASE expression used for Profit and Stock value calculations.
    /// Matches the original Powersoft365 logic 1:1 (PriceID 1..10, 99=Cost, 98=ItemCost,
    /// 88=AverageCost, 87=WeightedAverageCost). Falls back through @iDefaultPrice when the
    /// selected basis is unrecognised.
    /// </summary>
    private static string BuildPriceCaseExpr(string sParamName, string vatParamName)
    {
        var sb = new StringBuilder();
        sb.Append("(CASE ").Append(sParamName).AppendLine();
        for (int i = 1; i <= 10; i++)
        {
            sb.Append("    WHEN '").Append(i).Append("' THEN CASE WHEN ")
              .Append(vatParamName).Append(" = 0 THEN ISNULL(it.Price").Append(i).Append("Excl,0) ELSE ISNULL(it.Price")
              .Append(i).AppendLine("Incl,0) END");
        }
        sb.AppendLine("    WHEN '99' THEN ISNULL(it.Cost,0)");
        sb.AppendLine("    WHEN '98' THEN ISNULL(d.ItemCost,0)");
        sb.AppendLine("    WHEN '88' THEN ISNULL(it.AverageCost,0)");
        sb.AppendLine("    WHEN '87' THEN ISNULL(it.WeightedAverageCost,0)");
        sb.AppendLine("    ELSE CASE @iDefaultPrice");
        for (int i = 1; i <= 10; i++)
        {
            sb.Append("        WHEN '").Append(i).Append("' THEN CASE WHEN ")
              .Append(vatParamName).Append(" = 0 THEN ISNULL(it.Price").Append(i).Append("Excl,0) ELSE ISNULL(it.Price")
              .Append(i).AppendLine("Incl,0) END");
        }
        sb.Append("        ELSE CASE WHEN ").Append(vatParamName)
          .AppendLine(" = 0 THEN ISNULL(it.Price1Excl,0) ELSE ISNULL(it.Price1Incl,0) END");
        sb.AppendLine("    END");
        sb.Append("  END)");
        return sb.ToString();
    }

    private void AppendTransactionLeg(
        StringBuilder sb, GroupingInfo grouping,
        string dateWhere, string filterCond, bool isSummary,
        CatalogueFilter filter,
        string detailTable, string headerTable, string headerJoin,
        string entityTable, string entityJoin,
        string entityCodeExpr, string entityNameExpr,
        string invoiceIdExpr, string invoiceType, int signMultiplier,
        string saleOnlyCond = "")
    {
        string sign = signMultiplier == -1 ? "(-1) * " : "";

        var invTypeDesc = invoiceType switch
        {
            "I" => "Sale", "C" => "Sale Return", "P" => "Purchase", "E" => "Purchase Return", _ => invoiceType
        };

        string ReplacePlaceholders(string expr) =>
            expr.Replace("__INVTYPE__", invoiceType)
                .Replace("__INVTYPEDESC__", invTypeDesc)
                .Replace("__INVID__", invoiceIdExpr)
                .Replace("__ENTITYCODE__", entityCodeExpr)
                .Replace("__ENTITYNAME__", entityNameExpr);

        sb.AppendLine("SELECT");
        sb.AppendLine($"  {ReplacePlaceholders(grouping.Level1Select)},");
        sb.AppendLine($"  {ReplacePlaceholders(grouping.Level2Select)},");
        sb.AppendLine($"  {ReplacePlaceholders(grouping.Level3Select)},");
        sb.AppendLine("  it.ItemCode,");
        sb.AppendLine("  it.ItemNamePrimary AS ItemDescription,");

        // Financial columns
        sb.AppendLine($"  {sign}ISNULL(d.Quantity, 0) AS Quantity,");
        sb.AppendLine($"  {sign}ISNULL(d.Amount, 0) AS ValueBeforeDiscount,");
        sb.AppendLine($"  {sign}(ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) AS Discount,");
        sb.AppendLine($"  {sign}(ISNULL(d.Amount,0) - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0))) AS NetValue,");
        sb.AppendLine($"  {sign}ISNULL(d.VatAmount, 0) AS VatAmount,");
        sb.AppendLine($"  {sign}(ISNULL(d.Amount,0) - (ISNULL(d.Discount,0) + ISNULL(d.ExtraDiscount,0)) + ISNULL(d.VatAmount,0)) AS GrossAmount,");
        var costExpr = ResolveCostExpr(filter.CostType);
        var profitPriceExpr = BuildPriceCaseExpr("@sProfitBasedOn", "@bProfitBasedOnIncludeVAT");
        var stockValuePriceExpr = BuildPriceCaseExpr("@sStockValueBasedOn", "@bStockValueBasedOnIncludeVAT");

        sb.AppendLine($"  {sign}ISNULL(d.ItemCost, 0) AS TransactionCost,");
        sb.AppendLine($"  {costExpr} AS Cost,");
        sb.AppendLine($"  {sign}({costExpr} * ISNULL(d.Quantity, 0)) AS TotalCost,");
        sb.AppendLine($"  {sign}({profitPriceExpr} * ISNULL(d.Quantity, 0)) AS ProfitValue,");
        sb.AppendLine($"  ISNULL(it.TotalStockQty, 0) AS TotalStockQty,");
        sb.AppendLine($"  ({stockValuePriceExpr} * ISNULL(it.TotalStockQty, 0)) AS TotalStockValue,");

        if (!isSummary)
        {
            // Entity
            sb.AppendLine($"  {entityCodeExpr} AS EntityCode,");
            sb.AppendLine($"  {entityNameExpr} AS EntityName,");
            // Invoice
            sb.AppendLine($"  CAST({invoiceIdExpr} AS NVARCHAR(50)) AS InvoiceNumber,");
            sb.AppendLine($"  '{invoiceType}' AS InvoiceType,");
            // Store
            sb.AppendLine("  ISNULL(h.fk_StoreCode,'') AS StoreCode,");
            sb.AppendLine("  ISNULL(s.StoreName,'') AS StoreName,");
            // Time
            sb.AppendLine("  CONVERT(DATE, h.DateTrans) AS DateTrans,");
            sb.AppendLine("  ISNULL(h.fk_UserCode,'') AS UserCode,");
            // Classification
            sb.AppendLine("  ISNULL(itcat.CategoryCode,'') AS ItemCategoryCode,");
            sb.AppendLine("  ISNULL(itcat.CategoryDescr,'') AS ItemCategoryDescr,");
            sb.AppendLine("  ISNULL(itdept.DepartmentCode,'') AS ItemDepartmentCode,");
            sb.AppendLine("  ISNULL(itdept.DepartmentDescr,'') AS ItemDepartmentDescr,");
            sb.AppendLine("  ISNULL(m.ModelCode,'') AS ModelCode,");
            sb.AppendLine("  CASE WHEN col.pk_ColourID IS NULL THEN '' ELSE ISNULL(col.ColourCode,'') + ' - ' + ISNULL(col.ColourName,'') END AS Colour,");
            sb.AppendLine("  CASE WHEN sz.pk_SizeID IS NULL THEN '' ELSE CASE WHEN ISNULL(sz.SizeInvoiceDescr,'') = '' THEN sz.SizeName ELSE sz.SizeInvoiceDescr END END AS Size,");
            sb.AppendLine("  ISNULL(brand.BrandDesc,'') AS BrandName,");
            sb.AppendLine("  ISNULL(season.SeasonDesc,'') AS SeasonName,");
            sb.AppendLine("  ISNULL(rs.fk_SupplierNo,'') AS ItemSupplierCode,");
            sb.AppendLine("  CASE WHEN sup.pk_SupplierNo IS NULL THEN '' ELSE CASE WHEN ISNULL(sup.Company,0) = 1 THEN ISNULL(sup.LastCompanyName,'') ELSE ISNULL(sup.FirstName,'') + ' ' + ISNULL(sup.LastCompanyName,'') END END AS ItemSupplierName,");
            // Prices
            sb.AppendLine("  ISNULL(it.Price1Excl,0) AS Price1Excl, ISNULL(it.Price1Incl,0) AS Price1Incl,");
            sb.AppendLine("  ISNULL(it.Price2Excl,0) AS Price2Excl, ISNULL(it.Price2Incl,0) AS Price2Incl,");
            sb.AppendLine("  ISNULL(it.Price3Excl,0) AS Price3Excl, ISNULL(it.Price3Incl,0) AS Price3Incl,");
            // Attrs
            sb.AppendLine("  ISNULL(a1.FieldDetailDescr,'') AS ItemAttr1Descr,");
            sb.AppendLine("  ISNULL(a2.FieldDetailDescr,'') AS ItemAttr2Descr,");
            sb.AppendLine("  ISNULL(a3.FieldDetailDescr,'') AS ItemAttr3Descr,");
            sb.AppendLine("  ISNULL(a4.FieldDetailDescr,'') AS ItemAttr4Descr,");
            sb.AppendLine("  ISNULL(a5.FieldDetailDescr,'') AS ItemAttr5Descr,");
            sb.AppendLine("  ISNULL(a6.FieldDetailDescr,'') AS ItemAttr6Descr");
        }
        else
        {
            sb.AppendLine("  '' AS EntityCode, '' AS EntityName,");
            sb.AppendLine("  '' AS InvoiceNumber, '' AS InvoiceType,");
            sb.AppendLine("  '' AS StoreCode, '' AS StoreName,");
            sb.AppendLine("  NULL AS DateTrans, '' AS UserCode,");
            sb.AppendLine("  '' AS ItemCategoryCode, '' AS ItemCategoryDescr,");
            sb.AppendLine("  '' AS ItemDepartmentCode, '' AS ItemDepartmentDescr,");
            sb.AppendLine("  '' AS ModelCode, '' AS Colour, '' AS Size,");
            sb.AppendLine("  '' AS BrandName, '' AS SeasonName,");
            sb.AppendLine("  '' AS ItemSupplierCode, '' AS ItemSupplierName,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price1Excl, CAST(0 AS DECIMAL(18,4)) AS Price1Incl,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price2Excl, CAST(0 AS DECIMAL(18,4)) AS Price2Incl,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price3Excl, CAST(0 AS DECIMAL(18,4)) AS Price3Incl,");
            sb.AppendLine("  '' AS ItemAttr1Descr, '' AS ItemAttr2Descr, '' AS ItemAttr3Descr,");
            sb.AppendLine("  '' AS ItemAttr4Descr, '' AS ItemAttr5Descr, '' AS ItemAttr6Descr");
        }

        // FROM + JOINs
        sb.AppendLine($"FROM {detailTable} d");
        sb.AppendLine($"INNER JOIN {headerTable} h ON {headerJoin}");
        sb.AppendLine("INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID");
        sb.AppendLine("LEFT JOIN tbl_RelItemSuppliers rs ON it.pk_ItemID = rs.fk_ItemID AND ISNULL(rs.PrimarySupplier,0) = 1");

        if (!isSummary)
        {
            sb.AppendLine("LEFT JOIN tbl_Store s ON h.fk_StoreCode = s.pk_StoreCode");
            sb.AppendLine($"LEFT JOIN {entityTable} e ON {entityJoin}");
            sb.AppendLine("LEFT JOIN tbl_ItemCategory itcat ON it.fk_CategoryID = itcat.pk_CategoryID");
            sb.AppendLine("LEFT JOIN tbl_ItemDepartment itdept ON it.fk_DepartmentID = itdept.pk_DepartmentID");
            sb.AppendLine("LEFT JOIN tbl_Brands brand ON it.fk_BrandID = brand.pk_BrandID");
            sb.AppendLine("LEFT JOIN tbl_Season season ON it.fk_SeasonID = season.pk_SeasonID");
            sb.AppendLine("LEFT JOIN tbl_Model m ON it.fk_ModelID = m.pk_ModelID");
            sb.AppendLine("LEFT JOIN tbl_Colour col ON it.fk_ColourID = col.pk_ColourID");
            sb.AppendLine("LEFT JOIN tbl_Size sz ON it.fk_SizeID = sz.pk_SizeID");
            sb.AppendLine("LEFT JOIN tbl_Supplier sup ON rs.fk_SupplierNo = sup.pk_SupplierNo");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a1 ON it.fk_AttrID1 = a1.pk_FieldDetailID");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a2 ON it.fk_AttrID2 = a2.pk_FieldDetailID");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a3 ON it.fk_AttrID3 = a3.pk_FieldDetailID");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a4 ON it.fk_AttrID4 = a4.pk_FieldDetailID");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a5 ON it.fk_AttrID5 = a5.pk_FieldDetailID");
            sb.AppendLine("LEFT JOIN tbl_FieldDetail a6 ON it.fk_AttrID6 = a6.pk_FieldDetailID");
        }

        // Grouping-specific joins (needed in both modes for Level expressions)
        // In detailed mode, skip joins whose alias is already present from the detail block
        foreach (var rawLine in grouping.JoinLines)
        {
            if (string.IsNullOrEmpty(rawLine)) continue;
            var joinLine = rawLine
                .Replace("__ENTITYJOIN__", $"LEFT JOIN {entityTable} e ON {entityJoin}");
            if (!isSummary && (joinLine.Contains(" e ON ") || joinLine.Contains(" s ON ")))
                continue;
            sb.AppendLine(joinLine);
        }

        // Ensure entity alias 'e' is joined in summary mode when sale-only filters reference e.PostalCode.
        // In detailed mode the entity is already joined above (line ~416).
        if (isSummary && !string.IsNullOrEmpty(saleOnlyCond) && saleOnlyCond.Contains("e.", StringComparison.Ordinal))
        {
            sb.AppendLine($"LEFT JOIN {entityTable} e ON {entityJoin}");
        }

        sb.AppendLine($"{dateWhere}{filterCond}{saleOnlyCond}");
    }

    private string BuildOuterQuery(CatalogueFilter filter, GroupingInfo grouping, string innerUnion, bool isSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SELECT");
        sb.AppendLine("  r.Level1Code, r.Level1Description, r.Level2Code, r.Level2Description, r.Level3Code, r.Level3Description,");

        bool allNone = filter.PrimaryGroup == CatalogueGroupBy.None
                    && filter.SecondaryGroup == CatalogueGroupBy.None
                    && filter.ThirdGroup == CatalogueGroupBy.None;

        if (!isSummary || allNone)
            sb.AppendLine("  r.ItemCode, r.ItemDescription,");
        else
            sb.AppendLine("  NULL AS ItemCode, NULL AS ItemDescription,");

        // Aggregated financial columns
        sb.AppendLine("  SUM(r.Quantity) AS Quantity,");
        sb.AppendLine("  SUM(r.ValueBeforeDiscount) AS ValueBeforeDiscount,");
        sb.AppendLine("  SUM(r.Discount) AS Discount,");
        sb.AppendLine("  SUM(r.NetValue) AS NetValue,");
        sb.AppendLine("  SUM(r.VatAmount) AS VatAmount,");
        sb.AppendLine("  SUM(r.GrossAmount) AS GrossAmount,");
        sb.AppendLine("  SUM(r.ProfitValue) AS ProfitValue,");
        sb.AppendLine("  SUM(r.TransactionCost) AS TransactionCost,");
        sb.AppendLine("  MAX(r.Cost) AS Cost,");
        sb.AppendLine("  SUM(r.TotalCost) AS TotalCost,");

        if (isSummary && !allNone)
        {
            sb.AppendLine("  SUM(r.TotalStockQty) AS TotalStockQty,");
            sb.AppendLine("  SUM(r.TotalStockValue) AS TotalStockValue,");
        }
        else
        {
            sb.AppendLine("  MAX(r.TotalStockQty) AS TotalStockQty,");
            sb.AppendLine("  MAX(r.TotalStockValue) AS TotalStockValue,");
        }

        if (!isSummary || allNone)
        {
            sb.AppendLine("  r.EntityCode, r.EntityName,");
            sb.AppendLine("  r.InvoiceNumber, r.InvoiceType,");
            sb.AppendLine("  r.StoreCode, r.StoreName,");
            sb.AppendLine("  r.DateTrans, r.UserCode,");
            sb.AppendLine("  r.ItemCategoryCode, r.ItemCategoryDescr,");
            sb.AppendLine("  r.ItemDepartmentCode, r.ItemDepartmentDescr,");
            sb.AppendLine("  r.ModelCode, r.Colour, r.Size,");
            sb.AppendLine("  r.BrandName, r.SeasonName,");
            sb.AppendLine("  r.ItemSupplierCode, r.ItemSupplierName,");
            sb.AppendLine("  r.Price1Excl, r.Price1Incl,");
            sb.AppendLine("  r.Price2Excl, r.Price2Incl,");
            sb.AppendLine("  r.Price3Excl, r.Price3Incl,");
            sb.AppendLine("  r.ItemAttr1Descr, r.ItemAttr2Descr, r.ItemAttr3Descr,");
            sb.AppendLine("  r.ItemAttr4Descr, r.ItemAttr5Descr, r.ItemAttr6Descr");
        }
        else
        {
            sb.AppendLine("  '' AS EntityCode, '' AS EntityName,");
            sb.AppendLine("  '' AS InvoiceNumber, '' AS InvoiceType,");
            sb.AppendLine("  '' AS StoreCode, '' AS StoreName,");
            sb.AppendLine("  NULL AS DateTrans, '' AS UserCode,");
            sb.AppendLine("  '' AS ItemCategoryCode, '' AS ItemCategoryDescr,");
            sb.AppendLine("  '' AS ItemDepartmentCode, '' AS ItemDepartmentDescr,");
            sb.AppendLine("  '' AS ModelCode, '' AS Colour, '' AS Size,");
            sb.AppendLine("  '' AS BrandName, '' AS SeasonName,");
            sb.AppendLine("  '' AS ItemSupplierCode, '' AS ItemSupplierName,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price1Excl, CAST(0 AS DECIMAL(18,4)) AS Price1Incl,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price2Excl, CAST(0 AS DECIMAL(18,4)) AS Price2Incl,");
            sb.AppendLine("  CAST(0 AS DECIMAL(18,4)) AS Price3Excl, CAST(0 AS DECIMAL(18,4)) AS Price3Incl,");
            sb.AppendLine("  '' AS ItemAttr1Descr, '' AS ItemAttr2Descr, '' AS ItemAttr3Descr,");
            sb.AppendLine("  '' AS ItemAttr4Descr, '' AS ItemAttr5Descr, '' AS ItemAttr6Descr");
        }

        sb.AppendLine($"FROM ({innerUnion}) r");

        // GROUP BY
        var groupByCols = new List<string>
        {
            "r.Level1Code", "r.Level1Description",
            "r.Level2Code", "r.Level2Description",
            "r.Level3Code", "r.Level3Description"
        };

        if (!isSummary || allNone)
        {
            groupByCols.AddRange(new[]
            {
                "r.ItemCode", "r.ItemDescription",
                "r.EntityCode", "r.EntityName",
                "r.InvoiceNumber", "r.InvoiceType",
                "r.StoreCode", "r.StoreName",
                "r.DateTrans", "r.UserCode",
                "r.ItemCategoryCode", "r.ItemCategoryDescr",
                "r.ItemDepartmentCode", "r.ItemDepartmentDescr",
                "r.ModelCode", "r.Colour", "r.Size",
                "r.BrandName", "r.SeasonName",
                "r.ItemSupplierCode", "r.ItemSupplierName",
                "r.Price1Excl", "r.Price1Incl",
                "r.Price2Excl", "r.Price2Incl",
                "r.Price3Excl", "r.Price3Incl",
                "r.ItemAttr1Descr", "r.ItemAttr2Descr", "r.ItemAttr3Descr",
                "r.ItemAttr4Descr", "r.ItemAttr5Descr", "r.ItemAttr6Descr"
            });
        }

        sb.AppendLine($"GROUP BY {string.Join(", ", groupByCols)}");

        return sb.ToString();
    }

    private string BuildTotalsQuery(CatalogueFilter filter, ItemFilterInfo itemFilters)
    {
        var dateWhere = BuildDateWhere(filter);
        var filterCond = itemFilters.WhereClause;
        var saleOnlyCond = itemFilters.SaleOnlyWhereClause;
        var sb = new StringBuilder();

        var costExpr = ResolveCostExpr(filter.CostType);
        var profitPriceExpr = BuildPriceCaseExpr("@sProfitBasedOn", "@bProfitBasedOnIncludeVAT");
        var stockValuePriceExpr = BuildPriceCaseExpr("@sStockValueBasedOn", "@bStockValueBasedOnIncludeVAT");

        // For sale legs we additionally LEFT JOIN tbl_Customer (alias e) so that PostalCode / Customer / Agent
        // filters in saleOnlyCond can be evaluated here too (keeps grid totals consistent with filtered rows).
        void AppendTotalsLeg(string detailTbl, string headerTbl, string hdrJoin, int sign, bool isSaleLeg)
        {
            string s = sign == -1 ? "(-1)*" : "";
            sb.AppendLine($"SELECT {s}ISNULL(d.Quantity,0) AS Qty,");
            sb.AppendLine($"  {s}ISNULL(d.Amount,0) AS ValBD,");
            sb.AppendLine($"  {s}(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0)) AS Disc,");
            sb.AppendLine($"  {s}(ISNULL(d.Amount,0)-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))) AS NV,");
            sb.AppendLine($"  {s}ISNULL(d.VatAmount,0) AS VA,");
            sb.AppendLine($"  {s}(ISNULL(d.Amount,0)-(ISNULL(d.Discount,0)+ISNULL(d.ExtraDiscount,0))+ISNULL(d.VatAmount,0)) AS GA,");
            sb.AppendLine($"  {s}ISNULL(d.ItemCost,0) AS TC,");
            sb.AppendLine($"  {s}({costExpr}*ISNULL(d.Quantity,0)) AS TCost,");
            sb.AppendLine($"  {s}({profitPriceExpr}*ISNULL(d.Quantity,0)) AS PV,");
            sb.AppendLine($"  ISNULL(it.TotalStockQty,0) AS SQ,");
            sb.AppendLine($"  ({stockValuePriceExpr}*ISNULL(it.TotalStockQty,0)) AS SV,");
            sb.AppendLine($"  it.pk_ItemID AS ItemID");
            sb.AppendLine($"FROM {detailTbl} d");
            sb.AppendLine($"INNER JOIN {headerTbl} h ON {hdrJoin}");
            sb.AppendLine("INNER JOIN tbl_Item it ON d.fk_ItemID = it.pk_ItemID");
            sb.AppendLine("LEFT JOIN tbl_RelItemSuppliers t4 ON it.pk_ItemID = t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0) = 1");
            if (isSaleLeg && !string.IsNullOrEmpty(saleOnlyCond))
                sb.AppendLine("LEFT JOIN tbl_Customer e ON h.fk_CustomerCode = e.pk_CustomerNo");
            sb.AppendLine($"{dateWhere}{filterCond}{(isSaleLeg ? saleOnlyCond : "")}");
        }

        sb.AppendLine(";WITH AllLegs AS (");

        bool needsUnion = false;
        if (filter.ReportOn == CatalogueReportOn.Sale || filter.ReportOn == CatalogueReportOn.Both)
        {
            AppendTotalsLeg("tbl_InvoiceDetails", "tbl_InvoiceHeader", "d.fk_Invoice = h.pk_InvoiceID", 1, isSaleLeg: true);
            sb.AppendLine("UNION ALL");
            AppendTotalsLeg("tbl_CreditDetails", "tbl_CreditHeader", "d.fk_Credit = h.pk_CreditID", -1, isSaleLeg: true);
            needsUnion = true;
        }
        if (filter.ReportOn == CatalogueReportOn.Purchase || filter.ReportOn == CatalogueReportOn.Both)
        {
            if (needsUnion) sb.AppendLine("UNION ALL");
            AppendTotalsLeg("tbl_PurchInvoiceDetails", "tbl_PurchInvoiceHeader", "d.fk_PurchInvoiceID = h.pk_PurchInvoiceID", 1, isSaleLeg: false);
            sb.AppendLine("UNION ALL");
            AppendTotalsLeg("tbl_PurchReturnDetails", "tbl_PurchReturnHeader", "d.fk_PurchReturnID = h.pk_PurchReturnID", -1, isSaleLeg: false);
        }

        sb.AppendLine("), StockPerItem AS (");
        // Per-item snapshot: collapse multiple transaction rows for the same item to one max value
        sb.AppendLine("  SELECT ItemID, MAX(SQ) AS MaxSQ, MAX(SV) AS MaxSV FROM AllLegs GROUP BY ItemID");
        sb.AppendLine(")");
        sb.AppendLine("SELECT");
        sb.AppendLine("  ISNULL(SUM(Qty),0) AS TotalQuantity,");
        sb.AppendLine("  ISNULL(SUM(ValBD),0) AS TotalValueBeforeDiscount,");
        sb.AppendLine("  ISNULL(SUM(Disc),0) AS TotalDiscount,");
        sb.AppendLine("  ISNULL(SUM(NV),0) AS TotalNetValue,");
        sb.AppendLine("  ISNULL(SUM(VA),0) AS TotalVatAmount,");
        sb.AppendLine("  ISNULL(SUM(GA),0) AS TotalGrossAmount,");
        sb.AppendLine("  ISNULL(SUM(TC),0) AS TotalTransactionCost,");
        sb.AppendLine("  ISNULL(SUM(TCost),0) AS TotalTotalCost,");
        sb.AppendLine("  ISNULL(SUM(PV),0) AS TotalProfitValue,");
        sb.AppendLine("  ISNULL((SELECT SUM(MaxSQ) FROM StockPerItem),0) AS TotalStockQty,");
        sb.AppendLine("  ISNULL((SELECT SUM(MaxSV) FROM StockPerItem),0) AS TotalStockValue");
        sb.AppendLine("FROM AllLegs");

        return sb.ToString();
    }

    #endregion

    #region Grouping

    private record GroupingInfo(
        string Level1Select, string Level2Select, string Level3Select,
        string JoinClause, List<string> JoinLines, bool HasAnyGroup);

    private GroupingInfo BuildGrouping(CatalogueFilter filter)
    {
        var g1 = GetGroupExpression(filter.PrimaryGroup, 1, filter.ReportOn);
        var g2 = GetGroupExpression(filter.SecondaryGroup, 2, filter.ReportOn);
        var g3 = GetGroupExpression(filter.ThirdGroup, 3, filter.ReportOn);

        bool hasAny = filter.PrimaryGroup != CatalogueGroupBy.None
                   || filter.SecondaryGroup != CatalogueGroupBy.None
                   || filter.ThirdGroup != CatalogueGroupBy.None;

        var joinLines = new List<string>();
        void AddJoins(GroupFragment? gf)
        {
            if (gf?.Join == null) return;
            foreach (var line in gf.Join.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !joinLines.Contains(trimmed))
                    joinLines.Add(trimmed);
            }
        }
        AddJoins(g1);
        AddJoins(g2);
        AddJoins(g3);

        return new GroupingInfo(
            g1?.Select ?? "'' AS Level1Code, '' AS Level1Description",
            g2?.Select ?? "'' AS Level2Code, '' AS Level2Description",
            g3?.Select ?? "'' AS Level3Code, '' AS Level3Description",
            string.Join("\n", joinLines),
            joinLines,
            hasAny
        );
    }

    private record GroupFragment(string Select, string? Join);

    private GroupFragment? GetGroupExpression(CatalogueGroupBy group, int level, CatalogueReportOn reportOn)
    {
        if (group == CatalogueGroupBy.None) return null;

        var lc = $"Level{level}Code";
        var ld = $"Level{level}Description";
        var alias = $"g{level}";

        return group switch
        {
            CatalogueGroupBy.Store => new(
                $"ISNULL(h.fk_StoreCode,'N/A') AS {lc}, ISNULL(s.StoreName,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Store s ON h.fk_StoreCode = s.pk_StoreCode"),

            CatalogueGroupBy.Category => new(
                $"ISNULL({alias}.CategoryCode,'N/A') AS {lc}, ISNULL({alias}.CategoryDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_ItemCategory {alias} ON it.fk_CategoryID = {alias}.pk_CategoryID"),

            CatalogueGroupBy.Department => new(
                $"ISNULL({alias}.DepartmentCode,'N/A') AS {lc}, ISNULL({alias}.DepartmentDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_ItemDepartment {alias} ON it.fk_DepartmentID = {alias}.pk_DepartmentID"),

            CatalogueGroupBy.Supplier => new(
                $"ISNULL(rs.fk_SupplierNo,'N/A') AS {lc}, CASE WHEN {alias}.pk_SupplierNo IS NULL THEN 'N/A' ELSE CASE WHEN ISNULL({alias}.Company,0)=1 THEN ISNULL({alias}.LastCompanyName,'N/A') ELSE ISNULL({alias}.FirstName,'')+' '+ISNULL({alias}.LastCompanyName,'N/A') END END AS {ld}",
                $"LEFT JOIN tbl_Supplier {alias} ON rs.fk_SupplierNo = {alias}.pk_SupplierNo"),

            CatalogueGroupBy.Brand => new(
                $"ISNULL({alias}.BrandCode,'N/A') AS {lc}, ISNULL({alias}.BrandDesc,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Brands {alias} ON it.fk_BrandID = {alias}.pk_BrandID"),

            CatalogueGroupBy.Season => new(
                $"ISNULL({alias}.SeasonCode,'N/A') AS {lc}, ISNULL({alias}.SeasonDesc,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Season {alias} ON it.fk_SeasonID = {alias}.pk_SeasonID"),

            CatalogueGroupBy.Model => new(
                $"ISNULL({alias}.ModelCode,'N/A') AS {lc}, ISNULL({alias}.ModelNamePrimary,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Model {alias} ON it.fk_ModelID = {alias}.pk_ModelID"),

            CatalogueGroupBy.Colour => new(
                $"ISNULL({alias}.ColourCode,'N/A') AS {lc}, ISNULL({alias}.ColourName,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Colour {alias} ON it.fk_ColourID = {alias}.pk_ColourID"),

            CatalogueGroupBy.Size => new(
                $"ISNULL({alias}.SizeCode,'N/A') AS {lc}, ISNULL({alias}.SizeName,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Size {alias} ON it.fk_SizeID = {alias}.pk_SizeID"),

            CatalogueGroupBy.Customer => new(
                $"__ENTITYCODE__ AS {lc}, __ENTITYNAME__ AS {ld}",
                $"__ENTITYJOIN__"),

            CatalogueGroupBy.TransactionDate => new(
                $"CONVERT(NVARCHAR(10), h.DateTrans, 120) AS {lc}, CONVERT(NVARCHAR(10), h.DateTrans, 120) AS {ld}",
                null),

            CatalogueGroupBy.TransactionMonth => new(
                $"FORMAT(h.DateTrans, 'yyyy-MM') AS {lc}, FORMAT(h.DateTrans, 'yyyy-MM') AS {ld}",
                null),

            CatalogueGroupBy.InvoiceType => new(
                $"'__INVTYPE__' AS {lc}, '__INVTYPEDESC__' AS {ld}",
                null),

            CatalogueGroupBy.InvoiceNo => new(
                $"CAST(__INVID__ AS NVARCHAR(50)) AS {lc}, CAST(__INVID__ AS NVARCHAR(50)) AS {ld}",
                null),

            CatalogueGroupBy.PaymentType => new(
                $"ISNULL({alias}.pk_ptcode,'CREDIT') AS {lc}, ISNULL({alias}.ptdesc,'CREDIT') AS {ld}",
                $"LEFT JOIN tbl_paymtype {alias} ON ISNULL(h.fk_PayTypeCode,'CREDIT') = {alias}.pk_ptcode"),

            CatalogueGroupBy.Station => new(
                $"ISNULL(h.fk_StationCode,'N/A') AS {lc}, ISNULL({alias}.StationName,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Station {alias} ON h.fk_StoreCode = {alias}.fk_StoreCode AND ISNULL(h.fk_StationCode,'0001') = {alias}.fk_StationCode"),

            CatalogueGroupBy.Franchise => new(
                $"ISNULL(s.fk_FranchiseCode,'N/A') AS {lc}, ISNULL({alias}.FranchiseName,'N/A') AS {ld}",
                $"LEFT JOIN tbl_Store s ON h.fk_StoreCode = s.pk_StoreCode\nLEFT JOIN tbl_Franchise {alias} ON s.fk_FranchiseCode = {alias}.pk_FranchiseCode"),

            CatalogueGroupBy.User => new(
                $"ISNULL(h.fk_UserCode,'N/A') AS {lc}, ISNULL(h.fk_UserCode,'N/A') AS {ld}",
                null),

            CatalogueGroupBy.ZReport => new(
                $"ISNULL(h.fk_ZReport,'N/A') AS {lc}, ISNULL(h.fk_ZReport,'N/A') AS {ld}",
                null),

            CatalogueGroupBy.Agent => new(
                $"CAST(ISNULL(h.fk_AgentID,0) AS NVARCHAR(20)) AS {lc}, CASE WHEN h.fk_AgentID IS NULL THEN 'N/A' ELSE ISNULL({alias}.FirstName,'') + ' ' + ISNULL({alias}.LastName,'') END AS {ld}",
                $"LEFT JOIN tbl_Agent {alias} ON h.fk_AgentID = {alias}.pk_SystemNo"),

            CatalogueGroupBy.ItemAttr1 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID1 = {alias}.pk_FieldDetailID"),

            CatalogueGroupBy.ItemAttr2 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID2 = {alias}.pk_FieldDetailID"),

            CatalogueGroupBy.ItemAttr3 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID3 = {alias}.pk_FieldDetailID"),

            CatalogueGroupBy.ItemAttr4 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID4 = {alias}.pk_FieldDetailID"),

            CatalogueGroupBy.ItemAttr5 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID5 = {alias}.pk_FieldDetailID"),

            CatalogueGroupBy.ItemAttr6 => new(
                $"ISNULL({alias}.FieldDetailCode,'N/A') AS {lc}, ISNULL({alias}.FieldDetailDescr,'N/A') AS {ld}",
                $"LEFT JOIN tbl_FieldDetail {alias} ON it.fk_AttrID6 = {alias}.pk_FieldDetailID"),

            _ => null
        };
    }

    #endregion

    #region Item Filters

    /// <summary>
    /// Item-level filters.
    /// - WhereClause: conditions safe for every UNION leg (item, supplier, store etc.).
    /// - SaleOnlyWhereClause: conditions that reference sale-leg-only columns
    ///   (h.fk_CustomerCode, h.fk_AgentID, e.PostalCode) and MUST NOT be appended to
    ///   purchase legs since those headers/entities don't have those columns.
    ///   Mirrors original repPowerReportCatalogue.aspx.vb:3757/3788/3791 behaviour.
    /// </summary>
    private record ItemFilterInfo(string WhereClause, List<SqlParameter> Parameters, string SaleOnlyWhereClause = "");

    private ItemFilterInfo BuildItemFilters(CatalogueFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();
        int idx = 0;

        if (filter.StoreCodes.Count > 0)
        {
            var names = new List<string>();
            foreach (var s in filter.StoreCodes) { var p = $"@st{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, s)); }
            sb.Append($" AND h.fk_StoreCode IN ({string.Join(",", names)})");
        }

        if (filter.ItemIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in filter.ItemIds) { var p = $"@it{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, id)); }
            sb.Append($" AND d.fk_ItemID IN ({string.Join(",", names)})");
        }

        if (filter.CategoryIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var c in filter.CategoryIds) { var p = $"@cat{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, c)); }
            sb.Append($" AND it.fk_CategoryID IN ({string.Join(",", names)})");
        }

        if (filter.DepartmentIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var d in filter.DepartmentIds) { var p = $"@dep{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, d)); }
            sb.Append($" AND it.fk_DepartmentID IN ({string.Join(",", names)})");
        }

        if (filter.SupplierIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var s in filter.SupplierIds) { var p = $"@sup{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, s)); }
            sb.Append($" AND rs.fk_SupplierNo IN ({string.Join(",", names)})");
        }

        if (filter.BrandIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var b in filter.BrandIds) { var p = $"@br{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, b)); }
            sb.Append($" AND it.fk_BrandID IN ({string.Join(",", names)})");
        }

        if (filter.SeasonIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var s in filter.SeasonIds) { var p = $"@sea{idx++}"; names.Add(p); parms.Add(new SqlParameter(p, s)); }
            sb.Append($" AND it.fk_SeasonID IN ({string.Join(",", names)})");
        }

        var saleOnlyWhere = "";
        if (filter.ItemsSelection != null)
        {
            var cols = new DimensionFilterBuilder.ColumnMap(
                Category: "it.fk_CategoryID",
                Department: "it.fk_DepartmentID",
                Brand: "it.fk_BrandID",
                Season: "it.fk_SeasonID",
                Item: "d.fk_ItemID",
                Store: "h.fk_StoreCode",
                Supplier: "rs.fk_SupplierNo",
                ItemTableAlias: "it");
            var (dimWhere, dimParms) = DimensionFilterBuilder.Build(filter.ItemsSelection, cols, idx);
            sb.Append(dimWhere);
            parms.AddRange(dimParms);
            idx += dimParms.Count;

            // Sale-only leg columns (mirrors original repPowerReportCatalogue.aspx.vb:3757/3788/3791):
            //   Customer  -> h.fk_CustomerCode
            //   Agent     -> h.fk_AgentID
            //   PostalCode-> e.PostalCode (entity = tbl_Customer on sale legs)
            var (soWhere, soParms) = DimensionFilterBuilder.BuildSaleOnly(
                filter.ItemsSelection,
                customerColumn: "h.fk_CustomerCode",
                agentColumn: "h.fk_AgentID",
                postalCodeColumn: "e.PostalCode",
                startIdx: idx);
            saleOnlyWhere = soWhere;
            parms.AddRange(soParms);
        }

        return new ItemFilterInfo(sb.ToString(), parms, saleOnlyWhere);
    }

    #endregion

    #region Column Filters & Sorting

    private static readonly Dictionary<string, string> ColumnSqlMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ItemCode"] = "d.ItemCode",
        ["ItemDescription"] = "d.ItemDescription",
        ["Level1Code"] = "d.Level1Code",
        ["Level1Description"] = "d.Level1Description",
        ["Level2Code"] = "d.Level2Code",
        ["Level2Description"] = "d.Level2Description",
        ["Level3Code"] = "d.Level3Code",
        ["Level3Description"] = "d.Level3Description",
        ["Quantity"] = "d.Quantity",
        ["ValueBeforeDiscount"] = "d.ValueBeforeDiscount",
        ["Discount"] = "d.Discount",
        ["NetValue"] = "d.NetValue",
        ["VatAmount"] = "d.VatAmount",
        ["GrossAmount"] = "d.GrossAmount",
        ["ProfitValue"] = "d.ProfitValue",
        ["TransactionCost"] = "d.TransactionCost",
        ["TotalCost"] = "d.TotalCost",
        ["TotalStockQty"] = "d.TotalStockQty",
        ["EntityCode"] = "d.EntityCode",
        ["EntityName"] = "d.EntityName",
        ["StoreCode"] = "d.StoreCode",
        ["StoreName"] = "d.StoreName",
        ["BrandName"] = "d.BrandName",
        ["SeasonName"] = "d.SeasonName",
        ["ItemCategoryDescr"] = "d.ItemCategoryDescr",
        ["ItemDepartmentDescr"] = "d.ItemDepartmentDescr"
    };

    private static readonly HashSet<string> TextColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ItemCode", "ItemDescription", "Level1Code", "Level1Description",
        "Level2Code", "Level2Description", "Level3Code", "Level3Description",
        "EntityCode", "EntityName", "StoreCode", "StoreName",
        "BrandName", "SeasonName", "ItemCategoryDescr", "ItemDepartmentDescr"
    };

    private string ResolveSortExpression(CatalogueFilter filter)
    {
        var col = filter.SortColumn ?? "ItemCode";
        var dir = string.Equals(filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        if (!ColumnSqlMap.TryGetValue(col, out var sqlExpr))
            sqlExpr = "d.ItemCode";

        var parts = new List<string>();

        if (filter.PrimaryGroup != CatalogueGroupBy.None)
        {
            parts.Add("d.Level1Description");
            parts.Add("d.Level1Code");
        }
        if (filter.SecondaryGroup != CatalogueGroupBy.None)
        {
            parts.Add("d.Level2Description");
            parts.Add("d.Level2Code");
        }
        if (filter.ThirdGroup != CatalogueGroupBy.None)
        {
            parts.Add("d.Level3Description");
            parts.Add("d.Level3Code");
        }

        parts.Add($"{sqlExpr} {dir}");
        return string.Join(", ", parts);
    }

    private (string whereClause, List<SqlParameter> filterParams) BuildColumnFilterClause(CatalogueFilter filter)
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

    private List<SqlParameter> BuildCommonParameters(CatalogueFilter filter, ItemFilterInfo itemFilters)
    {
        // When UseDateTime is false, strip time components so date-only comparison matches original VB behaviour.
        // When UseDateTime is true, pass full DateTime values so BETWEEN can honour hour/minute precision.
        var dateFromParam = filter.UseDateTime ? filter.DateFrom : filter.DateFrom.Date;
        var dateToParam = filter.UseDateTime ? filter.DateTo : filter.DateTo.Date;

        var parms = new List<SqlParameter>
        {
            new("@DateFrom", dateFromParam),
            new("@DateTo", dateToParam),
            new("@sProfitBasedOn", ((int)filter.ProfitBasedOn).ToString(CultureInfo.InvariantCulture)),
            new("@bProfitBasedOnIncludeVAT", filter.ProfitIncludesVat ? (byte)1 : (byte)0),
            new("@sStockValueBasedOn", ((int)filter.StockValueBasedOn).ToString(CultureInfo.InvariantCulture)),
            new("@bStockValueBasedOnIncludeVAT", filter.StockValueIncludesVat ? (byte)1 : (byte)0),
            new("@iDefaultPrice", "1")
        };
        parms.AddRange(itemFilters.Parameters);
        return parms;
    }

    private static SqlParameter Clone(SqlParameter src) => new(src.ParameterName, src.Value);

    private async Task<int> ExecuteCountAsync(SqlConnection conn, string sql, List<SqlParameter> parameters)
    {
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parameters) cmd.Parameters.Add(Clone(p));
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<List<CatalogueRow>> ExecuteDataAsync(
        SqlConnection conn, string sql, List<SqlParameter> parameters,
        CatalogueFilter filter, bool isSummary)
    {
        var items = new List<CatalogueRow>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        foreach (var p in parameters) cmd.Parameters.Add(Clone(p));
        cmd.Parameters.AddWithValue("@Skip", filter.Skip);
        cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new CatalogueRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i)) continue;
                var name = reader.GetName(i);
                switch (name)
                {
                    case "Level1Code": row.Level1 = reader.GetString(i); break;
                    case "Level1Description": row.Level1Value = reader.GetString(i); break;
                    case "Level2Code": row.Level2 = reader.GetString(i); break;
                    case "Level2Description": row.Level2Value = reader.GetString(i); break;
                    case "Level3Code": row.Level3 = reader.GetString(i); break;
                    case "Level3Description": row.Level3Value = reader.GetString(i); break;
                    case "ItemCode": row.ItemCode = reader.GetString(i); break;
                    case "ItemDescription": row.ItemDescription = reader.GetString(i); break;
                    case "Quantity": row.Quantity = reader.GetDecimal(i); break;
                    case "ValueBeforeDiscount": row.ValueBeforeDiscount = reader.GetDecimal(i); break;
                    case "Discount": row.Discount = reader.GetDecimal(i); break;
                    case "NetValue": row.NetValue = reader.GetDecimal(i); break;
                    case "VatAmount": row.VatAmount = reader.GetDecimal(i); break;
                    case "GrossAmount": row.GrossAmount = reader.GetDecimal(i); break;
                    case "ProfitValue": row.ProfitValue = reader.GetDecimal(i); break;
                    case "TransactionCost": row.TransactionCost = reader.GetDecimal(i); break;
                    case "Cost": row.Cost = reader.GetDecimal(i); break;
                    case "TotalCost": row.TotalCost = reader.GetDecimal(i); break;
                    case "TotalStockQty": row.TotalStockQty = reader.GetDecimal(i); break;
                    case "TotalStockValue": row.TotalStockValue = reader.GetDecimal(i); break;
                    case "EntityCode": row.EntityCode = reader.GetString(i); break;
                    case "EntityName": row.EntityName = reader.GetString(i); break;
                    case "InvoiceNumber": row.InvoiceNumber = reader.GetString(i); break;
                    case "InvoiceType": row.InvoiceType = reader.GetString(i); break;
                    case "StoreCode": row.StoreCode = reader.GetString(i); break;
                    case "StoreName": row.StoreName = reader.GetString(i); break;
                    case "DateTrans":
                        row.DateTrans = reader.GetDateTime(i);
                        break;
                    case "UserCode": row.UserCode = reader.GetString(i); break;
                    case "ItemCategoryCode": row.ItemCategoryCode = reader.GetString(i); break;
                    case "ItemCategoryDescr": row.ItemCategoryDescr = reader.GetString(i); break;
                    case "ItemDepartmentCode": row.ItemDepartmentCode = reader.GetString(i); break;
                    case "ItemDepartmentDescr": row.ItemDepartmentDescr = reader.GetString(i); break;
                    case "ModelCode": row.ModelCode = reader.GetString(i); break;
                    case "Colour": row.Colour = reader.GetString(i); break;
                    case "Size": row.Size = reader.GetString(i); break;
                    case "BrandName": row.BrandName = reader.GetString(i); break;
                    case "SeasonName": row.SeasonName = reader.GetString(i); break;
                    case "ItemSupplierCode": row.ItemSupplierCode = reader.GetString(i); break;
                    case "ItemSupplierName": row.ItemSupplierName = reader.GetString(i); break;
                    case "Price1Excl": row.Price1Excl = reader.GetDecimal(i); break;
                    case "Price1Incl": row.Price1Incl = reader.GetDecimal(i); break;
                    case "Price2Excl": row.Price2Excl = reader.GetDecimal(i); break;
                    case "Price2Incl": row.Price2Incl = reader.GetDecimal(i); break;
                    case "Price3Excl": row.Price3Excl = reader.GetDecimal(i); break;
                    case "Price3Incl": row.Price3Incl = reader.GetDecimal(i); break;
                    case "ItemAttr1Descr": row.ItemAttr1Descr = reader.GetString(i); break;
                    case "ItemAttr2Descr": row.ItemAttr2Descr = reader.GetString(i); break;
                    case "ItemAttr3Descr": row.ItemAttr3Descr = reader.GetString(i); break;
                    case "ItemAttr4Descr": row.ItemAttr4Descr = reader.GetString(i); break;
                    case "ItemAttr5Descr": row.ItemAttr5Descr = reader.GetString(i); break;
                    case "ItemAttr6Descr": row.ItemAttr6Descr = reader.GetString(i); break;
                }
            }
            items.Add(row);
        }
        return items;
    }

    private static decimal GetDecimalSafe(SqlDataReader reader, string name)
    {
        var ord = reader.GetOrdinal(name);
        return reader.IsDBNull(ord) ? 0 : reader.GetDecimal(ord);
    }

    #endregion
}
