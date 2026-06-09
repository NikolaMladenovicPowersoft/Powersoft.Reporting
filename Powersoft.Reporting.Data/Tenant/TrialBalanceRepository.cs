using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// Trial Balance data access. Ported 1:1 from legacy Powersoft.CloudAccounting:
///   - Powersoft.CloudReports\TrialBalance.vb (merge + DR/CR display logic)
///   - Powersoft.CloudQueries\WQR.vb (GetFiscalYear / AllOpenBalances /
///     AccountsMovementsSummary / AccountsBalanceWithNoAgingAnalysis)
///
/// Sources: tbl_payments (journal lines), tbl_detailac (detail accounts),
/// tbl_coa (chart-of-accounts headers), tbl_acperiod (fiscal years).
/// NOT tbl_Journal. Signed balance = CASE WHEN da_dctype = tt_drcr THEN pamount ELSE -pamount END.
///
/// Verified against pswaESHOPMODAPRODEMO (FY 2025): closing DR total = closing CR total
/// = 23,851,181.33 (the trial balance closes), movement DR = movement CR = 2,204,336.14.
/// </summary>
public class TrialBalanceRepository : ITrialBalanceRepository
{
    private readonly string _connectionString;

    public TrialBalanceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(List<TrialBalanceRow> rows, bool fiscalYearFound)> GenerateAsync(TrialBalanceFilter filter)
    {
        var parms = new List<SqlParameter>
        {
            new("@AsAt", SqlDbType.Date) { Value = filter.AsAt.Date },
            new("@AsAtPlus1", SqlDbType.Date) { Value = filter.AsAt.Date.AddDays(1) }
        };

        // Optional account selection (CSV of tbl_DetailAc.pk_DetailID — nvarchar).
        var accFilter = "";
        var accList = SplitCsv(filter.SelectedAccounts);
        if (accList.Count > 0)
        {
            var names = new List<string>();
            for (int i = 0; i < accList.Count; i++)
            {
                var p = "@acc" + i;
                names.Add(p);
                parms.Add(new SqlParameter(p, SqlDbType.NVarChar, 50) { Value = accList[i] });
            }
            accFilter = " AND t1.pk_DetailID IN (" + string.Join(",", names) + ")";
        }

        // Optional header selection (CSV of tbl_COA.pk_Key — int).
        var hdrFilter = "";
        var hdrList = SplitCsvInt(filter.SelectedHeaders);
        if (hdrList.Count > 0)
        {
            var names = new List<string>();
            for (int i = 0; i < hdrList.Count; i++)
            {
                var p = "@hdr" + i;
                names.Add(p);
                parms.Add(new SqlParameter(p, SqlDbType.Int) { Value = hdrList[i] });
            }
            hdrFilter = " AND t1.fk_parent IN (" + string.Join(",", names) + ")";
        }

        // Same account/header filters but qualified against tbl_detailac (alias b) for the
        // OB-existence probe below.
        var obAccFilter = accFilter.Replace("t1.pk_DetailID", "b.pk_DetailID");
        var obHdrFilter = hdrFilter.Replace("t1.fk_parent", "b.fk_parent");

        var sql = $@"
SET NOCOUNT ON;
DECLARE @ap_from date, @ap_to date;
SELECT TOP 1 @ap_from = ap_from, @ap_to = ap_to
FROM tbl_acperiod
WHERE CONVERT(date, @AsAt) BETWEEN CONVERT(date, ap_from) AND CONVERT(date, ap_to)
ORDER BY ap_to ASC;

IF @ap_from IS NULL
BEGIN
    SELECT 0 AS FiscalYearFound WHERE 1 = 0;  -- empty marker resultset
    RETURN;
END;

SELECT 1 AS FiscalYearFound;  -- marker resultset (1 row)

-- Legacy quirk (TrialBalance.vb lines 91-94): if the ENTIRE opening-balance set is empty
-- for the current selection, movements are accumulated from 2000-01-01 instead of the
-- fiscal-year start. Otherwise movements start at @ap_from.
DECLARE @mov_from date = @ap_from;
IF NOT EXISTS (
    SELECT 1
    FROM tbl_payments a
    INNER JOIN tbl_detailac b ON b.pk_DetailID = a.fk_tt_accode
    WHERE a.fk_tt_type = 'OB'
      AND CONVERT(date, a.pdate) >= @ap_from
      AND CONVERT(date, a.pdate) <= @ap_to{obAccFilter}{obHdrFilter}
)
    SET @mov_from = '20000101';

;WITH Accounts AS (
    SELECT t1.pk_DetailID, t1.da_name, t1.da_dctype, t1.fk_parent,
           t2.ac_name, t2.ac_prefix, t2.ac_number
    FROM tbl_DetailAc t1
    INNER JOIN tbl_COA t2 ON t1.fk_parent = t2.pk_Key
    WHERE 1 = 1{accFilter}{hdrFilter}
),
OB AS (
    -- Opening balance: only OB-type lines in the fiscal year. Legacy quirk: an account
    -- with more than one OB line is treated as opening balance 0 (foundRow.Length = 1).
    SELECT a.fk_tt_accode AS pk_detailid,
           CASE WHEN COUNT(*) = 1
                THEN SUM(CASE WHEN b.da_dctype = a.tt_drcr THEN a.pamount ELSE -a.pamount END)
                ELSE 0 END AS opbal
    FROM tbl_payments a
    INNER JOIN tbl_detailac b ON b.pk_DetailID = a.fk_tt_accode
    WHERE a.fk_tt_type = 'OB'
      AND CONVERT(date, a.pdate) >= @ap_from
      AND CONVERT(date, a.pdate) <= @ap_to
    GROUP BY a.fk_tt_accode
),
Mov AS (
    -- Period movements: fiscal-year start .. As At, excluding OB. Raw DR/CR sums (no sign flip).
    SELECT a.fk_tt_accode AS pk_detailid,
           SUM(CASE WHEN a.tt_drcr = 'DR' THEN a.pamount ELSE 0 END) AS drAmt,
           SUM(CASE WHEN a.tt_drcr = 'CR' THEN a.pamount ELSE 0 END) AS crAmt
    FROM tbl_payments a
    WHERE a.fk_tt_type <> 'OB'
      AND CONVERT(date, a.pdate) >= @mov_from
      AND CONVERT(date, a.pdate) <= CONVERT(date, @AsAt)
    GROUP BY a.fk_tt_accode
),
Clos AS (
    -- Closing balance: cumulative signed balance of all payments before As At + 1 day.
    SELECT a.fk_tt_accode AS pk_detailid,
           SUM(CASE WHEN b.da_dctype = a.tt_drcr THEN a.pamount ELSE -a.pamount END) AS bal
    FROM tbl_payments a
    INNER JOIN tbl_detailac b ON b.pk_DetailID = a.fk_tt_accode
    WHERE CONVERT(date, a.pdate) < CONVERT(date, @AsAtPlus1)
    GROUP BY a.fk_tt_accode
)
SELECT acc.pk_DetailID, acc.da_name, acc.da_dctype, acc.fk_parent,
       acc.ac_name, acc.ac_prefix, acc.ac_number,
       ISNULL(OB.opbal, 0)  AS opbal,
       ISNULL(Mov.drAmt, 0) AS drAmt,
       ISNULL(Mov.crAmt, 0) AS crAmt,
       ISNULL(Clos.bal, 0)  AS closbal
FROM Accounts acc
LEFT JOIN OB   ON OB.pk_detailid   = acc.pk_DetailID
LEFT JOIN Mov  ON Mov.pk_detailid  = acc.pk_DetailID
LEFT JOIN Clos ON Clos.pk_detailid = acc.pk_DetailID;";

        var suppressSet = new HashSet<int>(SplitCsvInt(filter.SuppressedHeaders));

        var rows = new List<TrialBalanceRow>();
        bool fiscalYearFound = false;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddRange(parms.ToArray());

        using var reader = await cmd.ExecuteReaderAsync();

        // First resultset: fiscal-year marker. 1 row => found; 0 rows => not found.
        if (await reader.ReadAsync())
            fiscalYearFound = true;

        if (!fiscalYearFound)
            return (rows, false);

        // Second resultset: the account rows.
        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            var dcType = GetStr(reader, "da_dctype");
            decimal opbal = GetDec(reader, "opbal");
            decimal drAmt = GetDec(reader, "drAmt");
            decimal crAmt = GetDec(reader, "crAmt");
            decimal closbal = GetDec(reader, "closbal");

            // Legacy zero-movement filter: opening + period movements only (NOT closing).
            if (!filter.IncludeZeroMovements && opbal == 0 && drAmt == 0 && crAmt == 0)
                continue;

            var prefix = (GetStr(reader, "ac_prefix")).Trim();
            var number = GetInt(reader, "ac_number").ToString().Trim();
            var headerCode = prefix + number;
            var headerCodeSort = prefix + number.PadLeft(10, '0');
            int fkParent = GetInt(reader, "fk_parent");

            var (openType, openAbs) = ToDisplay(opbal, dcType);
            var (closeType, closeAbs) = ToDisplay(closbal, dcType);

            rows.Add(new TrialBalanceRow
            {
                AccountCode = GetStr(reader, "pk_DetailID"),
                AccountName = GetStr(reader, "da_name"),
                OpeningBalance = openAbs,
                OpeningBalanceType = openType,
                DebitMovement = drAmt,
                CreditMovement = crAmt,
                ClosingBalance = closeAbs,
                ClosingBalanceType = closeType,
                HeaderKey = fkParent,
                HeaderName = GetStr(reader, "ac_name"),
                HeaderCode = headerCode,
                HeaderCodeSort = headerCodeSort,
                Suppressed = suppressSet.Contains(fkParent)
            });
        }

        // Legacy sort: ac_prefix, da_code. We use the padded header sort key then account code.
        rows = rows
            .OrderBy(r => r.HeaderCodeSort, StringComparer.Ordinal)
            .ThenBy(r => r.AccountCode, StringComparer.Ordinal)
            .ToList();

        return (rows, true);
    }

    public async Task<List<TrialBalanceHeaderOption>> GetHeadersAsync()
    {
        // Leaf headers that actually own detail accounts (mirrors legacy GetAllHeadersForSelection).
        const string sql = @"
SELECT c.pk_Key,
       LTRIM(RTRIM(ISNULL(c.ac_prefix,''))) + CAST(c.ac_number AS varchar(10)) AS ac_code,
       c.ac_name
FROM tbl_COA c
WHERE EXISTS (SELECT 1 FROM tbl_DetailAc d WHERE d.fk_parent = c.pk_Key)
ORDER BY c.ac_prefix, c.ac_number;";

        var list = new List<TrialBalanceHeaderOption>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TrialBalanceHeaderOption
            {
                Key = GetInt(reader, "pk_Key"),
                Code = GetStr(reader, "ac_code"),
                Name = GetStr(reader, "ac_name")
            });
        }
        return list;
    }

    public async Task<List<TrialBalanceAccountOption>> GetAccountsAsync(string? headersCsv)
    {
        var parms = new List<SqlParameter>();
        var hdrFilter = "";
        var hdrList = SplitCsvInt(headersCsv);
        if (hdrList.Count > 0)
        {
            var names = new List<string>();
            for (int i = 0; i < hdrList.Count; i++)
            {
                var p = "@hdr" + i;
                names.Add(p);
                parms.Add(new SqlParameter(p, SqlDbType.Int) { Value = hdrList[i] });
            }
            hdrFilter = " AND d.fk_parent IN (" + string.Join(",", names) + ")";
        }

        var sql = $@"
SELECT d.pk_DetailID, d.da_name, d.fk_parent
FROM tbl_DetailAc d
INNER JOIN tbl_COA c ON d.fk_parent = c.pk_Key
WHERE 1 = 1{hdrFilter}
ORDER BY d.da_name;";

        var list = new List<TrialBalanceAccountOption>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddRange(parms.ToArray());
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TrialBalanceAccountOption
            {
                Code = GetStr(reader, "pk_DetailID"),
                Name = GetStr(reader, "da_name"),
                HeaderKey = GetInt(reader, "fk_parent")
            });
        }
        return list;
    }

    /// <summary>
    /// Converts a signed balance into (displayType, absoluteValue) exactly as legacy TrialBalance.vb:
    ///   balance = 0                       -> ""
    ///   balance > 0 AND da_dctype = "DR"  -> "DR"
    ///   balance &lt; 0 AND da_dctype = "DR"  -> "CR"
    ///   balance > 0 AND da_dctype = "CR"  -> "CR"
    ///   else                              -> "DR"   (covers balance&lt;0 AND CR, and any other dctype)
    /// </summary>
    private static (string type, decimal abs) ToDisplay(decimal balance, string dcType)
    {
        string type;
        if (balance == 0)
            type = "";
        else if (balance > 0 && dcType == "DR")
            type = "DR";
        else if (balance < 0 && dcType == "DR")
            type = "CR";
        else if (balance > 0 && dcType == "CR")
            type = "CR";
        else
            type = "DR";

        return (type, Math.Abs(balance));
    }

    private static List<string> SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();
    }

    private static List<int> SplitCsvInt(string? csv)
    {
        var result = new List<int>();
        foreach (var part in SplitCsv(csv))
            if (int.TryParse(part, out var n))
                result.Add(n);
        return result;
    }

    private static string GetStr(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? "" : reader.GetValue(ord).ToString() ?? "";
    }

    private static decimal GetDec(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? 0m : Convert.ToDecimal(reader.GetValue(ord));
    }

    private static int GetInt(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? 0 : Convert.ToInt32(reader.GetValue(ord));
    }
}
