using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class OffersReportRepository : IOffersReportRepository
{
    private readonly string _connectionString;

    public OffersReportRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(List<OffersReportRow> rows, int totalRecords)> GetDataAsync(OffersReportFilter filter)
    {
        var (whereClause, parms) = BuildWhereAndParams(filter);
        var (l1Code, l1Descr) = ResolveGroupExpr(filter.PrimaryGroup);
        var (l2Code, l2Descr) = ResolveGroupExpr(filter.SecondaryGroup);

        var activeSql = BuildSelect("tbl_OfferHeader", "tbl_OfferDetails",
            l1Code, l1Descr, l2Code, l2Descr, whereClause, filter);

        string fullSql;
        if (filter.IncludeHistory)
        {
            var histWhere = ReplaceAliasesForHistory(whereClause);
            var (hl1Code, hl1Descr) = ResolveGroupExpr(filter.PrimaryGroup, "h", "sh", "hst", "hc", "hag");
            var (hl2Code, hl2Descr) = ResolveGroupExpr(filter.SecondaryGroup, "h", "sh", "hst", "hc", "hag");
            var histSql = BuildHistorySelect(hl1Code, hl1Descr, hl2Code, hl2Descr, histWhere, filter);

            fullSql = $"SELECT TOP ({filter.MaxRecords}) * FROM ({activeSql} UNION ALL {histSql}) _combined {BuildOrderByAlias(filter.SortColumn, filter.SortDirection)}";
        }
        else
        {
            fullSql = activeSql;
        }

        var countParts = new List<string>();
        countParts.Add($@"SELECT COUNT(*) FROM tbl_OfferHeader t
LEFT JOIN tbl_OrderStatus s ON t.fk_StatusID = s.pk_OrderStatusID AND s.TableName = 'tbl_Offer'
LEFT JOIN tbl_Store st ON t.fk_StoreCode = st.pk_StoreCode
LEFT JOIN tbl_Customer c ON t.fk_CustomerCode = c.pk_CustomerNo
LEFT JOIN tbl_Agent ag ON t.fk_AgentID = ag.pk_SystemNo
WHERE 1=1 {whereClause}");

        if (filter.IncludeHistory)
        {
            var histCountWhere = ReplaceAliasesForHistory(whereClause);
            countParts.Add($@"SELECT COUNT(*) FROM tbl_OfferHeaderHistory h
LEFT JOIN tbl_OrderStatus sh ON h.fk_StatusID = sh.pk_OrderStatusID AND sh.TableName = 'tbl_Offer'
LEFT JOIN tbl_Store hst ON h.fk_StoreCode = hst.pk_StoreCode
LEFT JOIN tbl_Customer hc ON h.fk_CustomerCode = hc.pk_CustomerNo
LEFT JOIN tbl_Agent hag ON h.fk_AgentID = hag.pk_SystemNo
WHERE 1=1 {histCountWhere}");
        }

        var countSql = string.Join(" + ", countParts.Select((p, i) => $"({p})"));
        countSql = $"SELECT {countSql}";

        int totalRecords;
        var rows = new List<OffersReportRow>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await using (var countCmd = new SqlCommand(countSql, conn))
        {
            foreach (var p in parms) countCmd.Parameters.Add(CloneParam(p));
            totalRecords = (int)(await countCmd.ExecuteScalarAsync() ?? 0);
        }

        await using (var dataCmd = new SqlCommand(fullSql, conn))
        {
            foreach (var p in parms) dataCmd.Parameters.Add(CloneParam(p));
            dataCmd.CommandTimeout = 120;

            await using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(MapRow(reader));
            }
        }

        return (rows, totalRecords);
    }

    private static OffersReportRow MapRow(SqlDataReader reader) => new()
    {
        Level1Code = GetStr(reader, "Level1Code"),
        Level1Descr = GetStr(reader, "Level1Descr"),
        Level2Code = GetStr(reader, "Level2Code"),
        Level2Descr = GetStr(reader, "Level2Descr"),
        OfferNo = GetStr(reader, "OfferNo"),
        StatusName = GetStr(reader, "StatusName"),
        StatusColor = GetStr(reader, "StatusColor"),
        DateTrans = GetNullableDateTime(reader, "DateTrans"),
        ValidUntil = GetNullableDateTime(reader, "ValidUntil"),
        DeliveryDate = GetNullableDateTime(reader, "DeliveryDate"),
        CustomerName = GetStr(reader, "CustomerName"),
        CustomerCode = GetStr(reader, "CustomerCode"),
        StoreName = GetStr(reader, "StoreName"),
        StoreCode = GetStr(reader, "StoreCode"),
        AgentName = GetStr(reader, "AgentName"),
        InvoiceTotal = GetDecimal(reader, "InvoiceTotal"),
        InvoiceVat = GetDecimal(reader, "InvoiceVat"),
        InvoiceTotalDiscount = GetDecimal(reader, "InvoiceTotalDiscount"),
        InvoiceGrandTotal = GetDecimal(reader, "InvoiceGrandTotal"),
        InvoiceDiscountPerc = GetDecimal(reader, "InvoiceDiscountPerc"),
        ItemCount = GetInt(reader, "ItemCount"),
        TotalQuantity = GetDecimal(reader, "TotalQuantity"),
        TotalItemCost = GetDecimal(reader, "TotalItemCost"),
        Comments = GetStr(reader, "Comments"),
        InternalNotes = GetStr(reader, "InternalNotes"),
        Printed = reader.GetBoolean(reader.GetOrdinal("Printed")),
        SentByEmail = reader.GetBoolean(reader.GetOrdinal("SentByEmail")),
        IsStandardOffer = reader.GetBoolean(reader.GetOrdinal("IsStandardOffer")),
        StandardOfferName = GetStr(reader, "StandardOfferName"),
        OrderPercentage = GetDecimal(reader, "OrderPercentage"),
        LinkedLead = GetStr(reader, "LinkedLead"),
        CreatedBy = GetStr(reader, "CreatedBy"),
        Source = GetStr(reader, "Source")
    };

    private static string BuildSelect(string headerTable, string detailTable,
        string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause, OffersReportFilter filter)
    {
        var sourceLabel = headerTable == "tbl_OfferHeader"
            ? "CASE WHEN ISNULL(t.IsStandardOffer,0) = 1 THEN 'Standard Offer' ELSE 'Offer' END"
            : "'History'";
        var orderBy = filter.IncludeHistory ? "" : BuildOrderBy(filter.SortColumn, filter.SortDirection);
        var top = filter.IncludeHistory ? "" : $"TOP ({filter.MaxRecords})";

        return $@"
SELECT {top}
    {l1Code} AS Level1Code,
    {l1Descr} AS Level1Descr,
    {l2Code} AS Level2Code,
    {l2Descr} AS Level2Descr,
    t.pk_OfferID AS OfferNo,
    ISNULL(s.OrderStatusName, '(No Status)') AS StatusName,
    ISNULL(s.OrderStatusHTML, '#999999') AS StatusColor,
    t.DateTrans,
    t.ValidUntil,
    t.DeliveryDate,
    CASE WHEN c.Company = 1 THEN ISNULL(c.LastCompanyName,'')
         ELSE ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastCompanyName,'') END AS CustomerName,
    ISNULL(t.fk_CustomerCode,'') AS CustomerCode,
    ISNULL(st.StoreName,'') AS StoreName,
    ISNULL(t.fk_StoreCode,'') AS StoreCode,
    CASE WHEN ag.pk_SystemNo IS NOT NULL
         THEN ISNULL(ag.FirstName,'') + ' ' + ISNULL(ag.LastName,'')
         ELSE '' END AS AgentName,
    ISNULL(t.InvoiceTotal, 0) AS InvoiceTotal,
    ISNULL(t.InvoiceVat, 0) AS InvoiceVat,
    (ISNULL(t.InvoiceTotalDiscount, 0) + ISNULL(t.InvoiceDiscount, 0) - ISNULL(t.InvoiceDiscountVAT, 0)) AS InvoiceTotalDiscount,
    ISNULL(t.InvoiceGrandTotal, 0) AS InvoiceGrandTotal,
    CASE WHEN ISNULL(t.InvoiceTotal, 0) <> 0
         THEN ((ISNULL(t.InvoiceTotalDiscount, 0) + ISNULL(t.InvoiceDiscount, 0) - ISNULL(t.InvoiceDiscountVAT, 0)) / t.InvoiceTotal) * 100
         ELSE 0 END AS InvoiceDiscountPerc,
    ISNULL(det.ItemCount, 0) AS ItemCount,
    ISNULL(det.TotalQuantity, 0) AS TotalQuantity,
    ISNULL(det.TotalItemCost, 0) AS TotalItemCost,
    ISNULL(t.Comments, '') AS Comments,
    ISNULL(t.InternalNotes, '') AS InternalNotes,
    ISNULL(t.Printed, 0) AS Printed,
    ISNULL(t.SentByEmail, 0) AS SentByEmail,
    ISNULL(t.IsStandardOffer, 0) AS IsStandardOffer,
    ISNULL(t.StandardOfferName, '') AS StandardOfferName,
    ISNULL(t.OrderPercentage, 0) AS OrderPercentage,
    ISNULL(t.fk_LeadNo, '') AS LinkedLead,
    ISNULL(t.fk_UserCode, '') AS CreatedBy,
    {sourceLabel} AS Source
FROM {headerTable} t
LEFT JOIN tbl_OrderStatus s ON t.fk_StatusID = s.pk_OrderStatusID AND s.TableName = 'tbl_Offer'
LEFT JOIN tbl_Store st ON t.fk_StoreCode = st.pk_StoreCode
LEFT JOIN tbl_Customer c ON t.fk_CustomerCode = c.pk_CustomerNo
LEFT JOIN tbl_Agent ag ON t.fk_AgentID = ag.pk_SystemNo
LEFT JOIN (
    SELECT fk_OfferID,
           COUNT(*) AS ItemCount,
           SUM(ISNULL(Quantity, 0)) AS TotalQuantity,
           SUM(ISNULL(ItemCost, 0) * ISNULL(Quantity, 0)) AS TotalItemCost
    FROM {detailTable}
    GROUP BY fk_OfferID
) det ON det.fk_OfferID = t.pk_OfferID
WHERE 1=1 {whereClause}
{orderBy}";
    }

    private static string BuildHistorySelect(string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause, OffersReportFilter filter)
    {
        return $@"
SELECT
    {l1Code} AS Level1Code,
    {l1Descr} AS Level1Descr,
    {l2Code} AS Level2Code,
    {l2Descr} AS Level2Descr,
    h.pk_OfferID AS OfferNo,
    ISNULL(sh.OrderStatusName, '(No Status)') AS StatusName,
    ISNULL(sh.OrderStatusHTML, '#999999') AS StatusColor,
    h.DateTrans,
    h.ValidUntil,
    h.DeliveryDate,
    CASE WHEN hc.Company = 1 THEN ISNULL(hc.LastCompanyName,'')
         ELSE ISNULL(hc.FirstName,'') + ' ' + ISNULL(hc.LastCompanyName,'') END AS CustomerName,
    ISNULL(h.fk_CustomerCode,'') AS CustomerCode,
    ISNULL(hst.StoreName,'') AS StoreName,
    ISNULL(h.fk_StoreCode,'') AS StoreCode,
    CASE WHEN hag.pk_SystemNo IS NOT NULL
         THEN ISNULL(hag.FirstName,'') + ' ' + ISNULL(hag.LastName,'')
         ELSE '' END AS AgentName,
    ISNULL(h.InvoiceTotal, 0) AS InvoiceTotal,
    ISNULL(h.InvoiceVat, 0) AS InvoiceVat,
    (ISNULL(h.InvoiceTotalDiscount, 0) + ISNULL(h.InvoiceDiscount, 0) - ISNULL(h.InvoiceDiscountVAT, 0)) AS InvoiceTotalDiscount,
    ISNULL(h.InvoiceGrandTotal, 0) AS InvoiceGrandTotal,
    CASE WHEN ISNULL(h.InvoiceTotal, 0) <> 0
         THEN ((ISNULL(h.InvoiceTotalDiscount, 0) + ISNULL(h.InvoiceDiscount, 0) - ISNULL(h.InvoiceDiscountVAT, 0)) / h.InvoiceTotal) * 100
         ELSE 0 END AS InvoiceDiscountPerc,
    ISNULL(hdet.ItemCount, 0) AS ItemCount,
    ISNULL(hdet.TotalQuantity, 0) AS TotalQuantity,
    ISNULL(hdet.TotalItemCost, 0) AS TotalItemCost,
    ISNULL(h.Comments, '') AS Comments,
    ISNULL(h.InternalNotes, '') AS InternalNotes,
    ISNULL(h.Printed, 0) AS Printed,
    ISNULL(h.SentByEmail, 0) AS SentByEmail,
    ISNULL(h.IsStandardOffer, 0) AS IsStandardOffer,
    ISNULL(h.StandardOfferName, '') AS StandardOfferName,
    ISNULL(h.OrderPercentage, 0) AS OrderPercentage,
    ISNULL(h.fk_LeadNo, '') AS LinkedLead,
    ISNULL(h.fk_UserCode, '') AS CreatedBy,
    'History' AS Source
FROM tbl_OfferHeaderHistory h
LEFT JOIN tbl_OrderStatus sh ON h.fk_StatusID = sh.pk_OrderStatusID AND sh.TableName = 'tbl_Offer'
LEFT JOIN tbl_Store hst ON h.fk_StoreCode = hst.pk_StoreCode
LEFT JOIN tbl_Customer hc ON h.fk_CustomerCode = hc.pk_CustomerNo
LEFT JOIN tbl_Agent hag ON h.fk_AgentID = hag.pk_SystemNo
LEFT JOIN (
    SELECT fk_OfferID,
           COUNT(*) AS ItemCount,
           SUM(ISNULL(Quantity, 0)) AS TotalQuantity,
           SUM(ISNULL(ItemCost, 0) * ISNULL(Quantity, 0)) AS TotalItemCost
    FROM tbl_OfferDetailsHistory
    GROUP BY fk_OfferID
) hdet ON hdet.fk_OfferID = h.pk_OfferID
WHERE 1=1 {whereClause}";
    }

    private static (string whereClause, List<SqlParameter> parms) BuildWhereAndParams(OffersReportFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();

        var dateCol = filter.DateField switch
        {
            "ValidUntil" => "t.ValidUntil",
            "SessionDateTime" => "t.SessionDateTime",
            "DeliveryDate" => "t.DeliveryDate",
            _ => "t.DateTrans"
        };
        sb.Append($" AND CONVERT(DATE, {dateCol}) BETWEEN @dFrom AND @dTo");
        parms.Add(new SqlParameter("@dFrom", System.Data.SqlDbType.Date) { Value = filter.DateFrom.Date });
        parms.Add(new SqlParameter("@dTo", System.Data.SqlDbType.Date) { Value = filter.DateTo.Date });

        if (!string.Equals(filter.StatusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND s.OrderStatusCode = @statusCode");
            parms.Add(new SqlParameter("@statusCode", System.Data.SqlDbType.NVarChar, 50) { Value = filter.StatusFilter });
        }

        if (!string.Equals(filter.StoreFilter, "All", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(filter.StoreFilter))
        {
            sb.Append(" AND t.fk_StoreCode = @storeCode");
            parms.Add(new SqlParameter("@storeCode", System.Data.SqlDbType.NVarChar, 50) { Value = filter.StoreFilter });
        }

        if (!string.Equals(filter.AgentFilter, "All", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(filter.AgentFilter, out var agentId))
        {
            sb.Append(" AND t.fk_AgentID = @agentId");
            parms.Add(new SqlParameter("@agentId", System.Data.SqlDbType.BigInt) { Value = agentId });
        }

        if (string.Equals(filter.OfferType, "Offers", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND ISNULL(t.IsStandardOffer, 0) = 0");
        }
        else if (string.Equals(filter.OfferType, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND t.IsStandardOffer = 1");
        }

        return (sb.ToString(), parms);
    }

    private static (string code, string descr) ResolveGroupExpr(string? group,
        string t = "t", string s = "s", string st = "st", string c = "c", string ag = "ag") =>
        (group?.ToUpperInvariant()) switch
        {
            "STATUS" => ($"ISNULL({s}.OrderStatusCode,'')", $"ISNULL({s}.OrderStatusName,'(No Status)')"),
            "STORE" => ($"ISNULL({t}.fk_StoreCode,'')", $"ISNULL({st}.StoreName,'(No Store)')"),
            "CUSTOMER" => ($"ISNULL({t}.fk_CustomerCode,'')",
                $"CASE WHEN {c}.Company = 1 THEN ISNULL({c}.LastCompanyName,'') ELSE ISNULL({c}.FirstName,'') + ' ' + ISNULL({c}.LastCompanyName,'') END"),
            "AGENT" => ($"CAST(ISNULL({t}.fk_AgentID,0) AS VARCHAR(20))",
                $"CASE WHEN {ag}.pk_SystemNo IS NOT NULL THEN ISNULL({ag}.FirstName,'') + ' ' + ISNULL({ag}.LastName,'') ELSE '(No Agent)' END"),
            "MONTH" => ($"FORMAT({t}.DateTrans,'yyyy-MM')", $"FORMAT({t}.DateTrans,'yyyy-MM')"),
            "YEAR" => ($"CAST(YEAR({t}.DateTrans) AS VARCHAR(4))", $"CAST(YEAR({t}.DateTrans) AS VARCHAR(4))"),
            _ => ("''", "''")
        };

    private static string BuildOrderBy(string sortColumn, string sortDirection)
    {
        var dir = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var col = sortColumn switch
        {
            "OfferNo" => "t.pk_OfferID",
            "CustomerName" => "CustomerName",
            "StoreName" => "st.StoreName",
            "AgentName" => "AgentName",
            "StatusName" => "s.OrderStatusName",
            "InvoiceGrandTotal" => "t.InvoiceGrandTotal",
            "InvoiceTotal" => "t.InvoiceTotal",
            "ItemCount" => "det.ItemCount",
            "TotalQuantity" => "det.TotalQuantity",
            "ValidUntil" => "t.ValidUntil",
            "DeliveryDate" => "t.DeliveryDate",
            "OrderPercentage" => "t.OrderPercentage",
            _ => "t.DateTrans"
        };
        return $"ORDER BY {col} {dir}";
    }

    private static string ReplaceAliasesForHistory(string whereClause) =>
        whereClause
            .Replace("t.DateTrans", "h.DateTrans")
            .Replace("t.ValidUntil", "h.ValidUntil")
            .Replace("t.SessionDateTime", "h.SessionDateTime")
            .Replace("t.DeliveryDate", "h.DeliveryDate")
            .Replace("t.fk_StoreCode", "h.fk_StoreCode")
            .Replace("t.fk_AgentID", "h.fk_AgentID")
            .Replace("t.IsStandardOffer", "h.IsStandardOffer")
            .Replace("s.OrderStatusCode", "sh.OrderStatusCode");

    private static string BuildOrderByAlias(string sortColumn, string sortDirection)
    {
        var dir = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var col = sortColumn switch
        {
            "OfferNo" => "OfferNo",
            "CustomerName" => "CustomerName",
            "StoreName" => "StoreName",
            "AgentName" => "AgentName",
            "StatusName" => "StatusName",
            "InvoiceGrandTotal" => "InvoiceGrandTotal",
            "InvoiceTotal" => "InvoiceTotal",
            "ItemCount" => "ItemCount",
            "TotalQuantity" => "TotalQuantity",
            "ValidUntil" => "ValidUntil",
            "DeliveryDate" => "DeliveryDate",
            "OrderPercentage" => "OrderPercentage",
            _ => "DateTrans"
        };
        return $"ORDER BY {col} {dir}";
    }

    private static SqlParameter CloneParam(SqlParameter src, string? suffix = null)
    {
        var name = suffix == null ? src.ParameterName : src.ParameterName + suffix;
        return new SqlParameter(name, src.SqlDbType, src.Size)
        {
            Value = src.Value,
            IsNullable = src.IsNullable
        };
    }

    private static string GetStr(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? "" : r.GetString(ord).Trim();
    }

    private static int GetInt(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? 0 : r.GetInt32(ord);
    }

    private static decimal GetDecimal(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDateTime(ord);
    }
}
