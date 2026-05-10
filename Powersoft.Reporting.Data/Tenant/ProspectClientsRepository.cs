using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class ProspectClientsRepository : IProspectClientsRepository
{
    private readonly string _connectionString;

    public ProspectClientsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(List<ProspectClientsRow> rows, int totalRecords)> GetDataAsync(ProspectClientsFilter filter)
    {
        var (whereClause, parms) = BuildWhereAndParams(filter);
        var (l1Code, l1Descr) = ResolveGroupExpr(filter.PrimaryGroup);
        var (l2Code, l2Descr) = ResolveGroupExpr(filter.SecondaryGroup);
        var orderBy = BuildOrderBy(filter);

        var innerSql = BuildSelect(l1Code, l1Descr, l2Code, l2Descr, whereClause, "Active");

        if (filter.IncludeHistory)
        {
            var historyWhere = whereClause.Replace("t.", "h.");
            historyWhere = historyWhere.Replace("s.", "sh.");
            historyWhere = historyWhere.Replace("p.", "ph.");
            historyWhere = historyWhere.Replace("af.", "haf.");
            historyWhere = historyWhere.Replace("c1.", "hc1.");
            historyWhere = historyWhere.Replace("c2.", "hc2.");
            var histSql = BuildHistorySelect(l1Code, l1Descr, l2Code, l2Descr, historyWhere);
            innerSql = $"({innerSql}) UNION ALL ({histSql})";
        }

        var countSb = new StringBuilder();
        countSb.Append("SELECT COUNT(*) FROM (");
        countSb.Append(innerSql);
        countSb.Append(") dt");

        var dataSb = new StringBuilder();
        dataSb.Append($"SELECT TOP ({filter.MaxRecords}) * FROM (");
        dataSb.Append(innerSql);
        dataSb.Append($") dt ORDER BY {orderBy}");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        int totalRecords;
        using (var countCmd = new SqlCommand(countSb.ToString(), conn))
        {
            countCmd.CommandTimeout = 120;
            countCmd.Parameters.AddRange(CloneParams(parms));
            totalRecords = (int)(await countCmd.ExecuteScalarAsync() ?? 0);
        }

        var rows = new List<ProspectClientsRow>();
        using (var dataCmd = new SqlCommand(dataSb.ToString(), conn))
        {
            dataCmd.CommandTimeout = 120;
            dataCmd.Parameters.AddRange(CloneParams(parms));
            using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new ProspectClientsRow
                {
                    LeadNo = GetStr(reader, "pk_LeadNo"),
                    Level1Code = GetStr(reader, "Level1Code"),
                    Level1Descr = GetStr(reader, "Level1Descr"),
                    Level2Code = GetStr(reader, "Level2Code"),
                    Level2Descr = GetStr(reader, "Level2Descr"),
                    IsCompany = reader.GetBoolean(reader.GetOrdinal("Company")),
                    CompanyName = GetStr(reader, "CompanyName"),
                    ContactPerson = GetStr(reader, "ContactPerson"),
                    ShortName = GetStr(reader, "ShortName"),
                    StatusName = GetStr(reader, "StatusName"),
                    StatusColor = GetStr(reader, "StatusColor"),
                    PriorityName = GetStr(reader, "PriorityName"),
                    PositionName = GetStr(reader, "PositionName"),
                    RegistrationDate = GetNullableDateTime(reader, "RegistrationDate"),
                    CreationDateTime = GetNullableDateTime(reader, "CreationDateTime"),
                    LastModification = GetNullableDateTime(reader, "LastModification"),
                    NextCommunicationDate = GetNullableDateTime(reader, "NextCommunicationDate"),
                    Tel1 = GetStr(reader, "Tel1"),
                    Tel2 = GetStr(reader, "Tel2"),
                    Mobile = GetStr(reader, "Mobile"),
                    Email = GetStr(reader, "Email"),
                    WebSite = GetStr(reader, "WebSite"),
                    Address = GetStr(reader, "Address"),
                    PostalCode = GetStr(reader, "PostalCode"),
                    Town = GetStr(reader, "Town"),
                    FollowedBy = GetStr(reader, "FollowedBy"),
                    RecommendedBy = GetStr(reader, "RecommendedBy"),
                    LinkedCustomer = GetStr(reader, "LinkedCustomer"),
                    Category1 = GetStr(reader, "Category1"),
                    Category2 = GetStr(reader, "Category2"),
                    Category3 = GetStr(reader, "Category3"),
                    Notes = GetStr(reader, "Notes"),
                    CreatedBy = GetStr(reader, "CreatedBy"),
                    LastModifiedBy = GetStr(reader, "LastModifiedBy"),
                    Source = GetStr(reader, "Source"),
                    OfferCount = GetInt(reader, "OfferCount"),
                    TotalOfferValue = GetDecimal(reader, "TotalOfferValue"),
                    EmailsSent = GetInt(reader, "EmailsSent"),
                    SmsSent = GetInt(reader, "SmsSent")
                });
            }
        }

        return (rows, totalRecords);
    }

    private static string BuildSelect(string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause, string sourceLabel)
    {
        return $@"
            SELECT t.pk_LeadNo
                ,{l1Code} AS Level1Code
                ,{l1Descr} AS Level1Descr
                ,{l2Code} AS Level2Code
                ,{l2Descr} AS Level2Descr
                ,t.Company
                ,CASE WHEN ISNULL(t.Company,0) = 0
                      THEN ISNULL(t.FirstName,'') + ' ' + ISNULL(t.LastCompanyName,'')
                      ELSE ISNULL(t.LastCompanyName,'')
                 END AS CompanyName
                ,ISNULL(t.ContactFirstName,'') + ' ' + ISNULL(t.ContactLastName,'') AS ContactPerson
                ,ISNULL(t.ShortName,'') AS ShortName
                ,ISNULL(s.OrderStatusName,'') AS StatusName
                ,ISNULL(s.OrderStatusHTML,'#999999') AS StatusColor
                ,ISNULL(p.FieldDetailDescr,'') AS PriorityName
                ,ISNULL(pos.PositionName,'') AS PositionName
                ,t.RegistrationDate
                ,t.CreationDateTime
                ,t.LastModification
                ,t.NextCommunicationDate
                ,ISNULL(t.Tel1,'') AS Tel1
                ,ISNULL(t.Tel2,'') AS Tel2
                ,ISNULL(t.Mobile,'') AS Mobile
                ,ISNULL(t.Email,'') AS Email
                ,ISNULL(t.WebSite,'') AS WebSite
                ,LTRIM(RTRIM(ISNULL(t.Address1,'') + ' ' + ISNULL(t.Address2,'') + ' ' + ISNULL(t.Address3,''))) AS Address
                ,ISNULL(t.PostalCode,'') AS PostalCode
                ,ISNULL(t.Town,'') AS Town
                ,CASE WHEN af.pk_SystemNo IS NOT NULL
                      THEN ISNULL(af.FirstName,'') + ' ' + ISNULL(af.LastName,'')
                      ELSE '' END AS FollowedBy
                ,ISNULL(r.RecommendedByName,'') AS RecommendedBy
                ,ISNULL(t.fk_CustomerNo,'') AS LinkedCustomer
                ,ISNULL(c1.CategoryDescr,'') AS Category1
                ,ISNULL(c2.CategoryDescr,'') AS Category2
                ,ISNULL(c3.CategoryDescr,'') AS Category3
                ,ISNULL(att.TextVal1,'') + CASE WHEN ISNULL(att.TextVal2,'') != '' THEN ' | ' + att.TextVal2 ELSE '' END
                 + CASE WHEN ISNULL(att.TextVal3,'') != '' THEN ' | ' + att.TextVal3 ELSE '' END
                 + CASE WHEN ISNULL(att.TextVal4,'') != '' THEN ' | ' + att.TextVal4 ELSE '' END
                 + CASE WHEN ISNULL(att.TextVal5,'') != '' THEN ' | ' + att.TextVal5 ELSE '' END
                 + CASE WHEN ISNULL(att.TextVal6,'') != '' THEN ' | ' + att.TextVal6 ELSE '' END AS Notes
                ,ISNULL(t.CreatedBy,'') AS CreatedBy
                ,ISNULL(t.LastModifiedBy,'') AS LastModifiedBy
                ,'{sourceLabel}' AS Source
                ,ISNULL(ofc.OfferCount, 0) AS OfferCount
                ,ISNULL(ofc.TotalOfferValue, 0) AS TotalOfferValue
                ,ISNULL(emc.EmailsSent, 0) AS EmailsSent
                ,ISNULL(smc.SmsSent, 0) AS SmsSent
            FROM dbo.tbl_Lead t
            LEFT JOIN tbl_OrderStatus s ON t.fk_StatusID = s.pk_OrderStatusID AND s.TableName = 'tbl_Leads'
            LEFT JOIN tbl_FieldDetail p ON t.fk_PriorityID = p.pk_FieldDetailID
            LEFT JOIN PBI_Dimension_Prospected_Customer_Position pos ON t.fk_OccupationID = pos.PositionID
            LEFT JOIN tbl_Agent af ON t.fk_FollowedById = af.pk_SystemNo
            LEFT JOIN tbl_CustCategory c1 ON t.fk_Category1 = c1.pk_CategoryID
            LEFT JOIN tbl_CustCategory c2 ON t.fk_Category2 = c2.pk_CategoryID
            LEFT JOIN tbl_CustCategory c3 ON t.fk_Category3 = c3.pk_CategoryID
            LEFT JOIN tbl_RelLeadAttributes att ON t.pk_LeadNo = att.fk_LeadNo
            LEFT JOIN (
                SELECT t0.pk_RecomID
                    ,CASE WHEN t0.Recommendation = 'CUSTOMER'
                          THEN CASE WHEN ISNULL(rc.Company,0) = 1 THEN rc.LastCompanyName
                               ELSE rc.FirstName + ' ' + rc.LastCompanyName END
                          WHEN t0.Recommendation = 'AGENT'
                          THEN ra.FirstName + ' ' + ra.LastName
                          ELSE t0.tk_RecommendSrc END AS RecommendedByName
                FROM tbl_RecommendSrc t0
                LEFT JOIN tbl_Customer rc ON t0.tk_RecommendSrc = rc.pk_CustomerNo AND t0.Recommendation = 'CUSTOMER'
                LEFT JOIN tbl_Agent ra ON t0.tk_RecommendSrc = CAST(ra.pk_SystemNo AS NVARCHAR(100)) AND t0.Recommendation = 'AGENT'
            ) r ON t.fk_RecomID = r.pk_RecomID
            LEFT JOIN (
                SELECT oh.fk_LeadNo
                    ,COUNT(DISTINCT oh.pk_OfferID) AS OfferCount
                    ,SUM(ISNULL(oh.InvoiceGrandTotal, 0)) AS TotalOfferValue
                FROM tbl_OfferHeader oh
                WHERE oh.fk_LeadNo IS NOT NULL AND oh.fk_LeadNo != ''
                GROUP BY oh.fk_LeadNo
            ) ofc ON t.pk_LeadNo = ofc.fk_LeadNo
            LEFT JOIN (
                SELECT ed.EmailCSCode, COUNT(*) AS EmailsSent
                FROM tbl_EmailTransactionDetail ed
                WHERE ed.EmailType = 'L' AND ed.EmailFailed = 0
                GROUP BY ed.EmailCSCode
            ) emc ON t.pk_LeadNo = emc.EmailCSCode
            LEFT JOIN (
                SELECT sd.SMSCSCode, COUNT(*) AS SmsSent
                FROM tbl_SMSTransactionDetail sd
                WHERE sd.SMSType = 'L' AND sd.SMSFailed = 0
                GROUP BY sd.SMSCSCode
            ) smc ON t.pk_LeadNo = smc.SMSCSCode
            WHERE {whereClause}";
    }

    private static string BuildHistorySelect(string l1Code, string l1Descr, string l2Code, string l2Descr,
        string whereClause)
    {
        return $@"
            SELECT h.pk_LeadNo
                ,{l1Code.Replace("t.", "h.").Replace("s.", "sh.").Replace("p.", "ph.").Replace("af.", "haf.").Replace("pos.", "hpos.").Replace("c1.", "hc1.").Replace("c2.", "hc2.").Replace("c3.", "hc3.")} AS Level1Code
                ,{l1Descr.Replace("t.", "h.").Replace("s.", "sh.").Replace("p.", "ph.").Replace("af.", "haf.").Replace("pos.", "hpos.").Replace("c1.", "hc1.").Replace("c2.", "hc2.").Replace("c3.", "hc3.")} AS Level1Descr
                ,{l2Code.Replace("t.", "h.").Replace("s.", "sh.").Replace("p.", "ph.").Replace("af.", "haf.").Replace("pos.", "hpos.").Replace("c1.", "hc1.").Replace("c2.", "hc2.").Replace("c3.", "hc3.")} AS Level2Code
                ,{l2Descr.Replace("t.", "h.").Replace("s.", "sh.").Replace("p.", "ph.").Replace("af.", "haf.").Replace("pos.", "hpos.").Replace("c1.", "hc1.").Replace("c2.", "hc2.").Replace("c3.", "hc3.")} AS Level2Descr
                ,h.Company
                ,CASE WHEN ISNULL(h.Company,0) = 0
                      THEN ISNULL(h.FirstName,'') + ' ' + ISNULL(h.LastCompanyName,'')
                      ELSE ISNULL(h.LastCompanyName,'')
                 END AS CompanyName
                ,ISNULL(h.ContactFirstName,'') + ' ' + ISNULL(h.ContactLastName,'') AS ContactPerson
                ,ISNULL(h.ShortName,'') AS ShortName
                ,ISNULL(sh.OrderStatusName,'') AS StatusName
                ,ISNULL(sh.OrderStatusHTML,'#999999') AS StatusColor
                ,ISNULL(ph.FieldDetailDescr,'') AS PriorityName
                ,ISNULL(hpos.PositionName,'') AS PositionName
                ,h.RegistrationDate
                ,h.CreationDateTime
                ,h.LastModification
                ,h.NextCommunicationDate
                ,ISNULL(h.Tel1,'') AS Tel1
                ,ISNULL(h.Tel2,'') AS Tel2
                ,ISNULL(h.Mobile,'') AS Mobile
                ,ISNULL(h.Email,'') AS Email
                ,ISNULL(h.WebSite,'') AS WebSite
                ,LTRIM(RTRIM(ISNULL(h.Address1,'') + ' ' + ISNULL(h.Address2,'') + ' ' + ISNULL(h.Address3,''))) AS Address
                ,ISNULL(h.PostalCode,'') AS PostalCode
                ,ISNULL(h.Town,'') AS Town
                ,CASE WHEN haf.pk_SystemNo IS NOT NULL
                      THEN ISNULL(haf.FirstName,'') + ' ' + ISNULL(haf.LastName,'')
                      ELSE '' END AS FollowedBy
                ,ISNULL(hr.RecommendedByName,'') AS RecommendedBy
                ,ISNULL(h.fk_CustomerNo,'') AS LinkedCustomer
                ,ISNULL(hc1.CategoryDescr,'') AS Category1
                ,ISNULL(hc2.CategoryDescr,'') AS Category2
                ,ISNULL(hc3.CategoryDescr,'') AS Category3
                ,ISNULL(hatt.TextVal1,'') + CASE WHEN ISNULL(hatt.TextVal2,'') != '' THEN ' | ' + hatt.TextVal2 ELSE '' END
                 + CASE WHEN ISNULL(hatt.TextVal3,'') != '' THEN ' | ' + hatt.TextVal3 ELSE '' END
                 + CASE WHEN ISNULL(hatt.TextVal4,'') != '' THEN ' | ' + hatt.TextVal4 ELSE '' END
                 + CASE WHEN ISNULL(hatt.TextVal5,'') != '' THEN ' | ' + hatt.TextVal5 ELSE '' END
                 + CASE WHEN ISNULL(hatt.TextVal6,'') != '' THEN ' | ' + hatt.TextVal6 ELSE '' END AS Notes
                ,ISNULL(h.CreatedBy,'') AS CreatedBy
                ,ISNULL(h.LastModifiedBy,'') AS LastModifiedBy
                ,'History' AS Source
                ,ISNULL(hofc.OfferCount, 0) AS OfferCount
                ,ISNULL(hofc.TotalOfferValue, 0) AS TotalOfferValue
                ,ISNULL(hemc.EmailsSent, 0) AS EmailsSent
                ,ISNULL(hsmc.SmsSent, 0) AS SmsSent
            FROM dbo.tbl_LeadHistory h
            LEFT JOIN tbl_OrderStatus sh ON h.fk_StatusID = sh.pk_OrderStatusID AND sh.TableName = 'tbl_Leads'
            LEFT JOIN tbl_FieldDetail ph ON h.fk_PriorityID = ph.pk_FieldDetailID
            LEFT JOIN PBI_Dimension_Prospected_Customer_Position hpos ON h.fk_OccupationID = hpos.PositionID
            LEFT JOIN tbl_Agent haf ON h.fk_FollowedById = haf.pk_SystemNo
            LEFT JOIN tbl_CustCategory hc1 ON h.fk_Category1 = hc1.pk_CategoryID
            LEFT JOIN tbl_CustCategory hc2 ON h.fk_Category2 = hc2.pk_CategoryID
            LEFT JOIN tbl_CustCategory hc3 ON h.fk_Category3 = hc3.pk_CategoryID
            LEFT JOIN tbl_RelLeadAttributesHistory hatt ON h.pk_LeadNo = hatt.fk_LeadNo
            LEFT JOIN (
                SELECT t0.pk_RecomID
                    ,CASE WHEN t0.Recommendation = 'CUSTOMER'
                          THEN CASE WHEN ISNULL(rc.Company,0) = 1 THEN rc.LastCompanyName
                               ELSE rc.FirstName + ' ' + rc.LastCompanyName END
                          WHEN t0.Recommendation = 'AGENT'
                          THEN ra.FirstName + ' ' + ra.LastName
                          ELSE t0.tk_RecommendSrc END AS RecommendedByName
                FROM tbl_RecommendSrc t0
                LEFT JOIN tbl_Customer rc ON t0.tk_RecommendSrc = rc.pk_CustomerNo AND t0.Recommendation = 'CUSTOMER'
                LEFT JOIN tbl_Agent ra ON t0.tk_RecommendSrc = CAST(ra.pk_SystemNo AS NVARCHAR(100)) AND t0.Recommendation = 'AGENT'
            ) hr ON h.fk_RecomID = hr.pk_RecomID
            LEFT JOIN (
                SELECT oh.fk_LeadNo
                    ,COUNT(DISTINCT oh.pk_OfferID) AS OfferCount
                    ,SUM(ISNULL(oh.InvoiceGrandTotal, 0)) AS TotalOfferValue
                FROM tbl_OfferHeader oh
                WHERE oh.fk_LeadNo IS NOT NULL AND oh.fk_LeadNo != ''
                GROUP BY oh.fk_LeadNo
            ) hofc ON h.pk_LeadNo = hofc.fk_LeadNo
            LEFT JOIN (
                SELECT ed.EmailCSCode, COUNT(*) AS EmailsSent
                FROM tbl_EmailTransactionDetail ed
                WHERE ed.EmailType = 'L' AND ed.EmailFailed = 0
                GROUP BY ed.EmailCSCode
            ) hemc ON h.pk_LeadNo = hemc.EmailCSCode
            LEFT JOIN (
                SELECT sd.SMSCSCode, COUNT(*) AS SmsSent
                FROM tbl_SMSTransactionDetail sd
                WHERE sd.SMSType = 'L' AND sd.SMSFailed = 0
                GROUP BY sd.SMSCSCode
            ) hsmc ON h.pk_LeadNo = hsmc.SMSCSCode
            WHERE {whereClause}";
    }

    private (string whereClause, List<SqlParameter> parms) BuildWhereAndParams(ProspectClientsFilter filter)
    {
        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();

        var dateCol = filter.DateField switch
        {
            "CreationDateTime" => "t.CreationDateTime",
            "LastModification" => "t.LastModification",
            "NextCommunicationDate" => "t.NextCommunicationDate",
            _ => "t.RegistrationDate"
        };

        sb.Append($"CONVERT(DATE, {dateCol}) BETWEEN @dFrom AND @dTo");
        parms.Add(new SqlParameter("@dFrom", System.Data.SqlDbType.Date) { Value = filter.DateFrom.Date });
        parms.Add(new SqlParameter("@dTo", System.Data.SqlDbType.Date) { Value = filter.DateTo.Date });

        if (!string.Equals(filter.StatusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND s.OrderStatusCode = @statusCode");
            parms.Add(new SqlParameter("@statusCode", filter.StatusFilter));
        }

        if (!string.Equals(filter.PriorityFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND p.FieldDetailCode = @priorityCode");
            parms.Add(new SqlParameter("@priorityCode", filter.PriorityFilter));
        }

        if (!string.Equals(filter.FollowedByFilter, "All", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(filter.FollowedByFilter, out var agentId))
        {
            sb.Append(" AND t.fk_FollowedById = @followedBy");
            parms.Add(new SqlParameter("@followedBy", System.Data.SqlDbType.BigInt) { Value = agentId });
        }

        if (!string.Equals(filter.Category1Filter, "All", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(filter.Category1Filter, out var cat1Id))
        {
            sb.Append(" AND t.fk_Category1 = @cat1");
            parms.Add(new SqlParameter("@cat1", System.Data.SqlDbType.BigInt) { Value = cat1Id });
        }

        if (!string.Equals(filter.Category2Filter, "All", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(filter.Category2Filter, out var cat2Id))
        {
            sb.Append(" AND t.fk_Category2 = @cat2");
            parms.Add(new SqlParameter("@cat2", System.Data.SqlDbType.BigInt) { Value = cat2Id });
        }

        return (sb.ToString(), parms);
    }

    private static (string code, string descr) ResolveGroupExpr(string group) => group?.ToUpperInvariant() switch
    {
        "STATUS" => ("ISNULL(s.OrderStatusCode,'')", "ISNULL(s.OrderStatusName,'(No Status)')"),
        "PRIORITY" => ("ISNULL(p.FieldDetailCode,'')", "ISNULL(p.FieldDetailDescr,'(No Priority)')"),
        "TOWN" => ("ISNULL(t.Town,'')", "ISNULL(t.Town,'(No Town)')"),
        "FOLLOWEDBY" => (
            "CAST(ISNULL(t.fk_FollowedById,0) AS VARCHAR(20))",
            "CASE WHEN af.pk_SystemNo IS NOT NULL THEN ISNULL(af.FirstName,'') + ' ' + ISNULL(af.LastName,'') ELSE '(Unassigned)' END"),
        "MONTH" => (
            "RIGHT('00'+CAST(DATEPART(month, t.RegistrationDate) AS VARCHAR(2)),2) + '-' + CAST(DATEPART(year, t.RegistrationDate) AS VARCHAR(4))",
            "RIGHT('00'+CAST(DATEPART(month, t.RegistrationDate) AS VARCHAR(2)),2) + '-' + CAST(DATEPART(year, t.RegistrationDate) AS VARCHAR(4))"),
        "YEAR" => (
            "CAST(DATEPART(year, t.RegistrationDate) AS VARCHAR(4))",
            "CAST(DATEPART(year, t.RegistrationDate) AS VARCHAR(4))"),
        "POSITION" => ("ISNULL(pos.PositionName,'')", "ISNULL(pos.PositionName,'(No Position)')"),
        "CATEGORY1" => ("CAST(ISNULL(t.fk_Category1,0) AS VARCHAR(20))", "ISNULL(c1.CategoryDescr,'(No Category 1)')"),
        "CATEGORY2" => ("CAST(ISNULL(t.fk_Category2,0) AS VARCHAR(20))", "ISNULL(c2.CategoryDescr,'(No Category 2)')"),
        _ => ("''", "''")
    };

    private static string BuildOrderBy(ProspectClientsFilter filter)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pk_LeadNo", "CompanyName", "StatusName", "PriorityName", "RegistrationDate",
            "CreationDateTime", "LastModification", "NextCommunicationDate", "Town",
            "FollowedBy", "RecommendedBy", "ContactPerson", "Email", "Level1Descr", "Level2Descr",
            "OfferCount", "TotalOfferValue", "EmailsSent", "SmsSent", "Source",
            "Category1", "Category2", "Notes"
        };

        var col = allowed.Contains(filter.SortColumn) ? filter.SortColumn : "RegistrationDate";
        var dir = string.Equals(filter.SortDirection, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        var parts = new List<string>();
        if (filter.PrimaryGroup != "NONE") parts.Add("Level1Descr");
        if (filter.SecondaryGroup != "NONE") parts.Add("Level2Descr");
        parts.Add($"{col} {dir}");

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

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? null : reader.GetDateTime(ord);
    }

    private static int GetInt(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);
    }

    private static decimal GetDecimal(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? 0m : reader.GetDecimal(ord);
    }
}
