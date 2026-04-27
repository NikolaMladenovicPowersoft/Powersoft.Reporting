using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class CancelLogRepository : ICancelLogRepository
{
    private readonly string _connectionString;

    public CancelLogRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private static readonly DimensionFilterBuilder.ColumnMap ClCols = new(
        Category: "t2.fk_CategoryID",
        Department: "t2.fk_DepartmentID",
        Brand: "t2.fk_BrandID",
        Season: "t2.fk_SeasonID",
        Item: "t2.pk_ItemID",
        Store: "t.fk_StoreCode",
        Supplier: "t4.fk_SupplierNo",
        Customer: "t.fk_CustomerCode",
        User: "t.fk_UserCode",
        Model: "t2.fk_ModelID",
        Colour: "t2.fk_ColourID",
        Size: "t2.fk_SizeID",
        GroupSize: "t3.fk_SizeGroupID",
        Fabric: "t3.fk_FabricID",
        Attr1: "t2.fk_AttrID1",
        Attr2: "t2.fk_AttrID2",
        Attr3: "t2.fk_AttrID3",
        Attr4: "t2.fk_AttrID4",
        Attr5: "t2.fk_AttrID5",
        Attr6: "t2.fk_AttrID6",
        ItemTableAlias: "t2");

    public async Task<(List<CancelLogDetailedRow> rows, int totalRecords)> GetDetailedAsync(CancelLogFilter filter)
    {
        var (whereClause, parms) = BuildWhereAndParams(filter);
        var (level1Code, level1Descr) = ResolveGroupExpr(filter.PrimaryGroup);
        var (level2Code, level2Descr) = ResolveGroupExpr(filter.SecondaryGroup);
        var orderBy = BuildOrderBy(filter);

        var countSb = new StringBuilder();
        countSb.Append("SELECT COUNT(*) FROM (");
        countSb.Append(BuildDetailedSelect(level1Code, level1Descr, level2Code, level2Descr, whereClause, filter));
        countSb.Append(") dt");

        var dataSb = new StringBuilder();
        dataSb.Append($"SELECT TOP ({filter.MaxRecords}) * FROM (");
        dataSb.Append(BuildDetailedSelect(level1Code, level1Descr, level2Code, level2Descr, whereClause, filter));
        dataSb.Append($") dt ORDER BY {orderBy}");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        int totalRecords;
        using (var countCmd = new SqlCommand(countSb.ToString(), conn))
        {
            countCmd.Parameters.AddRange(CloneParams(parms));
            totalRecords = (int)(await countCmd.ExecuteScalarAsync() ?? 0);
        }

        var rows = new List<CancelLogDetailedRow>();
        using (var dataCmd = new SqlCommand(dataSb.ToString(), conn))
        {
            dataCmd.Parameters.AddRange(CloneParams(parms));
            using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new CancelLogDetailedRow
                {
                    StoreAndStation = GetStr(reader, "StoreAndStation"),
                    Level1Code = GetStr(reader, "Level1Code"),
                    Level1Descr = GetStr(reader, "Level1Descr"),
                    Level2Code = GetStr(reader, "Level2Code"),
                    Level2Descr = GetStr(reader, "Level2Descr"),
                    ActionType = GetStr(reader, "ActionType"),
                    SessionDateTime = reader.IsDBNull(reader.GetOrdinal("SessionDateTime")) ? null : reader.GetDateTime(reader.GetOrdinal("SessionDateTime")),
                    StoreCode = GetStr(reader, "fk_StoreCode"),
                    StoreName = GetStr(reader, "StoreName"),
                    StationCode = GetStr(reader, "fk_StationCode"),
                    UserCode = GetStr(reader, "fk_UserCode"),
                    TransKind = GetStr(reader, "TransKind"),
                    CreditId = GetStr(reader, "fk_CreditID"),
                    InvoiceId = GetStr(reader, "fk_InvoiceID"),
                    CustomerCode = GetStr(reader, "fk_CustomerCode"),
                    CustomerFullName = GetStr(reader, "CustomerFullName"),
                    ItemCode = GetStr(reader, "ItemCode"),
                    ItemDescr = GetStr(reader, "ItemDescr"),
                    ZReport = GetStr(reader, "fk_ZReport"),
                    TotalInvoiceLines = reader.GetInt32(reader.GetOrdinal("TotalInvoiceLines")),
                    InvoiceTotal = reader.GetDecimal(reader.GetOrdinal("InvoiceTotal")),
                    Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity")),
                    Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                    TableNo = GetStr(reader, "TableNo"),
                    TableName = GetStr(reader, "TableName"),
                    TableNoPerson = reader.IsDBNull(reader.GetOrdinal("TableNoPerson")) ? 0 : reader.GetInt32(reader.GetOrdinal("TableNoPerson")),
                    CompartmentName = GetStr(reader, "CompartmentName")
                });
            }
        }

        return (rows, totalRecords);
    }

    public async Task<(List<CancelLogSummaryRow> rows, int totalRecords)> GetSummaryAsync(CancelLogFilter filter)
    {
        var (whereClause, parms) = BuildWhereAndParams(filter);
        var (level1Code, level1Descr) = ResolveGroupExpr(filter.PrimaryGroup);
        var (level2Code, level2Descr) = ResolveGroupExpr(filter.SecondaryGroup);

        var innerSelect = BuildSummaryInnerSelect(level1Code, level1Descr, level2Code, level2Descr, whereClause, filter);

        var countSb = new StringBuilder();
        countSb.Append("SELECT COUNT(*) FROM (");
        countSb.Append(BuildSummaryOuter(innerSelect));
        countSb.Append(") cf");

        var dataSb = new StringBuilder();
        dataSb.Append($"SELECT TOP ({filter.MaxRecords}) * FROM (");
        dataSb.Append(BuildSummaryOuter(innerSelect));
        dataSb.Append(") cf ORDER BY StoreAndStation, Level1Descr, Level2Descr");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        int totalRecords;
        using (var countCmd = new SqlCommand(countSb.ToString(), conn))
        {
            countCmd.Parameters.AddRange(CloneParams(parms));
            totalRecords = (int)(await countCmd.ExecuteScalarAsync() ?? 0);
        }

        var rows = new List<CancelLogSummaryRow>();
        using (var dataCmd = new SqlCommand(dataSb.ToString(), conn))
        {
            dataCmd.Parameters.AddRange(CloneParams(parms));
            using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new CancelLogSummaryRow
                {
                    StoreAndStation = GetStr(reader, "StoreAndStation"),
                    Level1Code = GetStr(reader, "Level1Code"),
                    Level1Descr = GetStr(reader, "Level1Descr"),
                    Level2Code = GetStr(reader, "Level2Code"),
                    Level2Descr = GetStr(reader, "Level2Descr"),
                    DeletedAction = reader.GetInt32(reader.GetOrdinal("DeletedAction")),
                    CancelledAction = reader.GetInt32(reader.GetOrdinal("CancelledAction")),
                    ComplimentaryAction = reader.GetInt32(reader.GetOrdinal("ComplimentaryAction")),
                    InvoiceTotal = reader.GetDecimal(reader.GetOrdinal("InvoiceTotal")),
                    Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity")),
                    Amount = reader.GetDecimal(reader.GetOrdinal("Amount"))
                });
            }
        }

        return (rows, totalRecords);
    }

    private string BuildDetailedSelect(string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause, CancelLogFilter filter)
    {
        return $@"
            SELECT t.fk_StoreCode + '/' + t.fk_StationCode + ' - ' + ISNULL(t5.StoreName,'') + '/' + ISNULL(t6.StationName,'') AS StoreAndStation
                ,{l1Code} AS Level1Code
                ,{l1Descr} AS Level1Descr
                ,{l2Code} AS Level2Code
                ,{l2Descr} AS Level2Descr
                ,CASE WHEN ISNULL(t.CancelKind,'') = 'D' THEN 'Deleted' WHEN ISNULL(t.CancelKind,'') = 'L' THEN 'Cancelled' ELSE 'Complimentary' END AS ActionType
                ,DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime) AS SessionDateTime
                ,t.fk_StoreCode
                ,t5.StoreName
                ,t.fk_StationCode
                ,t.fk_UserCode
                ,t.TransKind
                ,t.fk_CreditID
                ,t.fk_InvoiceID
                ,t.fk_CustomerCode
                ,CASE WHEN ISNULL(t1.Company,0) = 0 THEN t1.FirstName + ' ' + t1.LastCompanyName ELSE t1.LastCompanyName END AS CustomerFullName
                ,t.fk_ItemID
                ,t2.ItemCode
                ,t2.ItemNamePrimary AS ItemDescr
                ,ISNULL(t.fk_ZReport,'') AS fk_ZReport
                ,t.TotalInvoiceLines
                ,t.InvoiceTotal
                ,t.Quantity
                ,t.Amount
                ,ISNULL(t.TableNo,'') AS TableNo
                ,ISNULL(t.TableName,'') AS TableName
                ,ISNULL(t.TableNoPerson,0) AS TableNoPerson
                ,ISNULL(t.CompartmentName,'') AS CompartmentName
            FROM dbo.tbl_CancelLog t
            LEFT JOIN tbl_Customer t1 ON t.fk_CustomerCode = t1.pk_CustomerNo
            LEFT JOIN tbl_Item t2 ON t.fk_ItemID = t2.pk_ItemID
            LEFT JOIN tbl_Model t3 ON t2.fk_ModelID = t3.pk_ModelID
            LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID = t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0) = 1
            LEFT JOIN tbl_Store t5 ON t.fk_StoreCode = t5.pk_StoreCode
            LEFT JOIN tbl_Station t6 ON t.fk_StoreCode = t6.fk_StoreCode AND t.fk_StationCode = t6.fk_StationCode
            WHERE {whereClause}";
    }

    private string BuildSummaryInnerSelect(string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause, CancelLogFilter filter)
    {
        return $@"
            SELECT t.fk_StoreCode + '/' + t.fk_StationCode + ' - ' + ISNULL(t5.StoreName,'') + '/' + ISNULL(t6.StationName,'') AS StoreAndStation
                ,{l1Code} AS Level1Code
                ,{l1Descr} AS Level1Descr
                ,{l2Code} AS Level2Code
                ,{l2Descr} AS Level2Descr
                ,CASE WHEN ISNULL(t.CancelKind,'') = 'D' THEN 1 ELSE 0 END AS DeletedAction
                ,CASE WHEN ISNULL(t.CancelKind,0) = 'L' THEN 1 ELSE 0 END AS CancelledAction
                ,CASE WHEN ISNULL(t.CancelKind,0) = 'C' THEN 1 ELSE 0 END AS ComplimentaryAction
                ,t.InvoiceTotal
                ,t.Quantity
                ,t.Amount
            FROM dbo.tbl_CancelLog t
            LEFT JOIN tbl_Customer t1 ON t.fk_CustomerCode = t1.pk_CustomerNo
            LEFT JOIN tbl_Item t2 ON t.fk_ItemID = t2.pk_ItemID
            LEFT JOIN tbl_Model t3 ON t2.fk_ModelID = t3.pk_ModelID
            LEFT JOIN tbl_RelItemSuppliers t4 ON t2.pk_ItemID = t4.fk_ItemID AND ISNULL(t4.PrimarySupplier,0) = 1
            LEFT JOIN tbl_Store t5 ON t.fk_StoreCode = t5.pk_StoreCode
            LEFT JOIN tbl_Station t6 ON t.fk_StoreCode = t6.fk_StoreCode AND t.fk_StationCode = t6.fk_StationCode
            WHERE {whereClause}";
    }

    private static string BuildSummaryOuter(string innerSelect)
    {
        return $@"
            SELECT StoreAndStation, Level1Code, Level1Descr, Level2Code, Level2Descr,
                SUM(DeletedAction) AS DeletedAction,
                SUM(CancelledAction) AS CancelledAction,
                SUM(ComplimentaryAction) AS ComplimentaryAction,
                SUM(InvoiceTotal) AS InvoiceTotal,
                SUM(Quantity) AS Quantity,
                SUM(Amount) AS Amount
            FROM ({innerSelect}) dt
            GROUP BY StoreAndStation, Level1Code, Level1Descr, Level2Code, Level2Descr";
    }

    private (string whereClause, List<SqlParameter> parms) BuildWhereAndParams(CancelLogFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();

        if (filter.ReportByDateTime)
            sb.Append("CONVERT(datetime, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)) BETWEEN CONVERT(datetime, @dFrom) AND CONVERT(datetime, @dTo)");
        else
            sb.Append("CONVERT(date, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)) BETWEEN CONVERT(date, @dFrom) AND CONVERT(date, @dTo)");

        parms.Add(new SqlParameter("@dFrom", System.Data.SqlDbType.DateTime) { Value = filter.DateFrom });
        parms.Add(new SqlParameter("@dTo", System.Data.SqlDbType.DateTime) { Value = filter.DateTo });
        parms.Add(new SqlParameter("@tzOffsetMin", System.Data.SqlDbType.Int) { Value = filter.TimezoneOffsetMinutes });

        switch (filter.ActionType)
        {
            case CancelLogActionType.Deleted:
                sb.Append(" AND ISNULL(t.CancelKind,'') = 'D'");
                break;
            case CancelLogActionType.Cancelled:
                sb.Append(" AND ISNULL(t.CancelKind,'') = 'L'");
                break;
            case CancelLogActionType.Complimentary:
                sb.Append(" AND ISNULL(t.CancelKind,'') = 'C'");
                break;
        }

        var (dimWhere, dimParms) = DimensionFilterBuilder.Build(filter.ItemsSelection, ClCols);
        if (!string.IsNullOrEmpty(dimWhere))
        {
            sb.Append(dimWhere);
            parms.AddRange(dimParms);
        }

        if (filter.ItemsSelection?.ZReports.HasFilter == true)
        {
            var zrFilter = filter.ItemsSelection.ZReports;
            var zrIds = zrFilter.Ids.Where(id => id != "__NA__").ToList();
            bool hasNa = zrFilter.Ids.Contains("__NA__");
            if (zrFilter.Mode == FilterMode.Include)
            {
                if (zrIds.Count > 0 && hasNa)
                {
                    var names = AddZrParams(parms, zrIds);
                    sb.Append($" AND (ISNULL(t.fk_ZReport,'') IN ({string.Join(",", names)}) OR t.fk_ZReport IS NULL)");
                }
                else if (zrIds.Count > 0)
                {
                    var names = AddZrParams(parms, zrIds);
                    sb.Append($" AND ISNULL(t.fk_ZReport,'') IN ({string.Join(",", names)})");
                }
                else if (hasNa)
                    sb.Append(" AND t.fk_ZReport IS NULL");
            }
            else
            {
                if (zrIds.Count > 0 && hasNa)
                {
                    var names = AddZrParams(parms, zrIds);
                    sb.Append($" AND (ISNULL(t.fk_ZReport,'') NOT IN ({string.Join(",", names)}) AND t.fk_ZReport IS NOT NULL)");
                }
                else if (zrIds.Count > 0)
                {
                    var names = AddZrParams(parms, zrIds);
                    sb.Append($" AND ISNULL(t.fk_ZReport,'') NOT IN ({string.Join(",", names)})");
                }
                else if (hasNa)
                    sb.Append(" AND t.fk_ZReport IS NOT NULL");
            }
        }

        return (sb.ToString(), parms);
    }

    private static List<string> AddZrParams(List<SqlParameter> parms, List<string> ids)
    {
        var names = new List<string>();
        for (int i = 0; i < ids.Count; i++)
        {
            var p = $"@zr{parms.Count + i}";
            names.Add(p);
            parms.Add(new SqlParameter(p, ids[i]));
        }
        return names;
    }

    private static (string code, string descr) ResolveGroupExpr(string group) => group?.ToUpperInvariant() switch
    {
        "DATE" => (
            "CONVERT(varchar(10), DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime), 103)",
            "CONVERT(varchar(10), DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime), 103)"),
        "MONTH" => (
            "RIGHT('00'+CAST(DATEPART(month, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)) AS VARCHAR(2)),2) + '-' + CONVERT(varchar(4), DATEPART(year, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)))",
            "RIGHT('00'+CAST(DATEPART(month, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)) AS VARCHAR(2)),2) + '-' + CONVERT(varchar(4), DATEPART(year, DATEADD(minute, @tzOffsetMin, t.UTCSessionDateTime)))"),
        "CUST" => (
            "t.fk_CustomerCode",
            "t.fk_CustomerCode + ' - ' + CASE WHEN ISNULL(t1.Company,0) = 0 THEN t1.FirstName + ' ' + t1.LastCompanyName ELSE t1.LastCompanyName END"),
        "ACTION" => (
            "CASE WHEN ISNULL(t.Cancelled,0) = 0 THEN 'Deleted' ELSE 'Cancelled' END",
            "CASE WHEN ISNULL(t.Cancelled,0) = 0 THEN 'Deleted' ELSE 'Cancelled' END"),
        "STORE" => (
            "t.fk_StoreCode",
            "t.fk_StoreCode + ' - ' + ISNULL(t5.StoreName,'')"),
        "USER" => (
            "t.fk_UserCode",
            "t.fk_UserCode"),
        "ZREPORT" => (
            "ISNULL(t.fk_ZReport,'')",
            "ISNULL(t.fk_ZReport,'')"),
        _ => ("''", "''")
    };

    private static string BuildOrderBy(CancelLogFilter filter)
    {
        var parts = new List<string> { "StoreAndStation" };

        if (filter.PrimaryGroup != "NONE")
            parts.Add("Level1Descr");
        if (filter.SecondaryGroup != "NONE")
            parts.Add("Level2Descr");

        parts.Add("TableNo");
        parts.Add("SessionDateTime");
        parts.Add("ItemDescr");
        parts.Add("ItemCode");

        return string.Join(", ", parts);
    }

    private static SqlParameter[] CloneParams(List<SqlParameter> source)
    {
        return source.Select(p =>
        {
            var clone = new SqlParameter(p.ParameterName, p.SqlDbType) { Value = p.Value };
            if (p.Size > 0) clone.Size = p.Size;
            return clone;
        }).ToArray();
    }

    private static string GetStr(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? "" : reader.GetValue(ord).ToString() ?? "";
    }
}
