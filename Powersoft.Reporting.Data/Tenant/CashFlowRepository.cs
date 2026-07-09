using System.Data;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// Cash Flow (Direct) data access — self-contained port of the Power BI cash-flow engine.
///
/// TRANSACTION SLICE (verified identical to the cent against dbo.GetAllTransactionsForBowerBI on
/// pswaDEMO365MODAPRO1): a journal transaction (fk_tt_number) is cash-flow relevant when it touches
/// at least one bank account (tbl_accbank) and its type is not 'OB' (opening balance) or 'YE'
/// (year-end). Every leg of such a transaction is included.
///
/// SECTION MAPPING (extracted 1:1 from HE11901-ARVO-Accounting.pbix / ChartCF_Direct + bridge):
/// each account code is matched against dboReportsAI.tbl_CashFlowMapping ranges with an INCLUSIVE
/// STRING comparison (CodeFrom &lt;= code &lt;= CodeTo — the same semantics as the PBI M range
/// match). When several ranges match, the MOST SPECIFIC wins: greatest CodeFrom, then
/// Group/Category sort order — this rule reproduces the PBIX bridge table exactly (4,945/4,945
/// account-line pairs). Unmatched accounts fall into "(Unassigned)" (PBI silently drops them; we
/// keep them visible so no cash movement can hide).
///
/// SIGN (verified to the cent against the PBI matrix, Jan 2025 + Jan 2022 columns): displayed
/// amount = -(raw journal sum) for EVERY line (raw = DR positive / CR negative). Receipts show
/// positive under Cash In, payments negative under Cash Out, Bank balances the rest — all groups
/// sum to exactly 0. (The PBI account-level drill-through uses a different per-account sign and
/// does not reconcile with its own matrix; we deliberately keep the matrix sign everywhere.)
///
/// BUDGET (documented deviation): PBI reads a manually-maintained Excel sheet (CashFlowBudget);
/// this engine reads tbl_AccBudgetDetails (unpivoted by month, months inside the period, negated
/// natural-balance sign) mapped through the same section mapping.
///
/// Sources: tbl_payments, tbl_detailac, tbl_coa, tbl_accbank, tbl_AccBudgetDetails,
/// tbl_AccBudgetKey, dboReportsAI.tbl_CashFlowMapping.
/// </summary>
public class CashFlowRepository : ICashFlowRepository
{
    private readonly string _connectionString;

    public CashFlowRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Most-specific-range-wins account -> statement line resolution (see class remarks).
    // {0} = the code expression to match.
    private const string MapApply = @"
OUTER APPLY (
    SELECT TOP 1 m.GroupName, m.GroupSortOrder, m.CategoryName, m.CategorySortOrder
    FROM dboReportsAI.tbl_CashFlowMapping m WITH (NOLOCK)
    WHERE {0} >= m.CodeFrom AND {0} <= m.CodeTo
    ORDER BY m.CodeFrom DESC, m.GroupSortOrder, m.CategorySortOrder, m.pk_MappingID
) map";

    public async Task<CashFlowResult> GenerateAsync(CashFlowFilter filter)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var result = new CashFlowResult
        {
            Categories = await QueryMappingCategoriesAsync(conn)
        };

        if (filter.Monthly)
        {
            result.Months = MonthKeys(filter.DateFrom, filter.DateTo);
            result.Rows = await QueryActualsMonthlyAsync(conn, filter.DateFrom, filter.DateTo);
            result.Rows = result.Rows
                .Where(r => r.Amount != 0m || (r.MonthAmounts?.Values.Any(v => v != 0m) ?? false))
                .ToList();
            return result;
        }

        // Keyed on (AccountCode, HeaderKey): account legs key on their code; header-level budget
        // lines key on ("", HeaderKey).
        var merged = new Dictionary<(string acc, long hdr), CashFlowRow>();

        foreach (var r in await QueryActualsAsync(conn, filter.DateFrom, filter.DateTo))
        {
            merged[(r.AccountCode, r.HeaderKey)] = r;
        }

        if (filter.CompareToLastYear)
        {
            foreach (var p in await QueryActualsAsync(conn, filter.PriorDateFrom, filter.PriorDateTo))
            {
                if (merged.TryGetValue((p.AccountCode, p.HeaderKey), out var cur))
                    cur.PriorAmount = p.Amount;
                else
                {
                    p.PriorAmount = p.Amount;
                    p.Amount = 0m;
                    merged[(p.AccountCode, p.HeaderKey)] = p;
                }
            }
        }

        if (filter.IncludeBudget)
        {
            foreach (var b in await QueryBudgetAsync(conn, filter.DateFrom, filter.DateTo))
            {
                if (merged.TryGetValue((b.AccountCode, b.HeaderKey), out var cur))
                    cur.BudgetAmount += b.BudgetAmount;
                else
                    merged[(b.AccountCode, b.HeaderKey)] = b;
            }
        }

        // Drop all-zero account rows (DR/CR legs that cancel out exactly within the period, e.g.
        // VAT in/out) — they carry no information and only add noise to the statement.
        result.Rows = merged.Values
            .Where(r => r.Amount != 0m || r.PriorAmount != 0m || r.BudgetAmount != 0m)
            .ToList();

        return result;
    }

    private static List<string> MonthKeys(DateTime from, DateTime to)
    {
        var keys = new List<string>();
        var cur = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(to.Year, to.Month, 1);
        while (cur <= end)
        {
            keys.Add(cur.ToString("yyyy-MM"));
            cur = cur.AddMonths(1);
        }
        return keys;
    }

    private static async Task<List<CashFlowMappingCategory>> QueryMappingCategoriesAsync(SqlConnection conn)
    {
        const string sql = @"
SELECT GroupName, GroupSortOrder, CategoryName, CategorySortOrder
FROM dboReportsAI.tbl_CashFlowMapping WITH (NOLOCK)
GROUP BY GroupName, GroupSortOrder, CategoryName, CategorySortOrder
ORDER BY GroupSortOrder, CategorySortOrder;";

        var list = new List<CashFlowMappingCategory>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CashFlowMappingCategory
            {
                GroupName = GetStr(reader, "GroupName"),
                GroupSortOrder = GetInt(reader, "GroupSortOrder"),
                CategoryName = GetStr(reader, "CategoryName"),
                CategorySortOrder = GetInt(reader, "CategorySortOrder")
            });
        }
        return list;
    }

    // Shared FROM/WHERE for the actuals queries. Display sign: -(DR + / CR -) → receipts positive
    // (Cash In), payments negative (Cash Out).
    private static string ActualsSql(bool monthly)
    {
        var monthCol = monthly ? ",\n       DATEFROMPARTS(YEAR(t.pdate), MONTH(t.pdate), 1) AS MonthBucket" : "";
        var monthGrp = monthly ? ",\n         DATEFROMPARTS(YEAR(t.pdate), MONTH(t.pdate), 1)" : "";

        return $@"
;WITH CashTx AS (
    SELECT DISTINCT t.fk_tt_number
    FROM tbl_payments t WITH (NOLOCK)
    INNER JOIN tbl_accbank b WITH (NOLOCK) ON t.fk_tt_accode = b.fk_ba_link
),
BankAcc AS (
    SELECT DISTINCT fk_ba_link AS AccountCode
    FROM tbl_accbank WITH (NOLOCK)
    WHERE fk_ba_link IS NOT NULL
)
SELECT d.pk_detailid                       AS AccountCode,
       d.da_name                           AS AccountName,
       CAST(d.fk_parent AS bigint)         AS HeaderKey,
       ISNULL(map.GroupName, '')           AS GroupName,
       ISNULL(map.GroupSortOrder, 0)       AS GroupSortOrder,
       ISNULL(map.CategoryName, '')        AS CategoryName,
       ISNULL(map.CategorySortOrder, 0)    AS CategorySortOrder,
       CAST(CASE WHEN ba.AccountCode IS NULL THEN 0 ELSE 1 END AS bit) AS IsBank,
       CONVERT(decimal(18,2), SUM(CASE WHEN t.tt_drcr = 'DR' THEN -1 ELSE 1 END * t.pamount)) AS Amount{monthCol}
FROM tbl_payments t WITH (NOLOCK)
INNER JOIN tbl_detailac d WITH (NOLOCK) ON t.fk_tt_accode = d.pk_detailid
INNER JOIN CashTx cf ON t.fk_tt_number = cf.fk_tt_number
LEFT JOIN BankAcc ba ON ba.AccountCode = d.pk_detailid
{string.Format(MapApply, "d.pk_detailid")}
WHERE t.fk_tt_type NOT IN ('OB', 'YE')
  AND CONVERT(date, t.pdate) >= CONVERT(date, @from)
  AND CONVERT(date, t.pdate) <= CONVERT(date, @to)
GROUP BY d.pk_detailid, d.da_name, d.fk_parent,
         map.GroupName, map.GroupSortOrder, map.CategoryName, map.CategorySortOrder,
         ba.AccountCode{monthGrp};";
    }

    private static async Task<List<CashFlowRow>> QueryActualsAsync(SqlConnection conn, DateTime from, DateTime to)
    {
        var rows = new List<CashFlowRow>();
        using var cmd = new SqlCommand(ActualsSql(monthly: false), conn) { CommandTimeout = 120 };
        cmd.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = from.Date });
        cmd.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = to.Date });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadRow(reader));
        }
        return rows;
    }

    private static async Task<List<CashFlowRow>> QueryActualsMonthlyAsync(SqlConnection conn, DateTime from, DateTime to)
    {
        var byKey = new Dictionary<(string acc, long hdr), CashFlowRow>();
        using var cmd = new SqlCommand(ActualsSql(monthly: true), conn) { CommandTimeout = 120 };
        cmd.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = from.Date });
        cmd.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = to.Date });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var r = ReadRow(reader);
            var monthKey = reader.GetDateTime(reader.GetOrdinal("MonthBucket")).ToString("yyyy-MM");
            var amount = r.Amount;

            if (!byKey.TryGetValue((r.AccountCode, r.HeaderKey), out var acc))
            {
                acc = r;
                acc.Amount = 0m;
                acc.MonthAmounts = new Dictionary<string, decimal>();
                byKey[(r.AccountCode, r.HeaderKey)] = acc;
            }
            acc.MonthAmounts![monthKey] = acc.MonthAmounts.GetValueOrDefault(monthKey) + amount;
            acc.Amount += amount; // row total across the period
        }
        return byKey.Values.ToList();
    }

    private static CashFlowRow ReadRow(SqlDataReader reader) => new()
    {
        AccountCode = GetStr(reader, "AccountCode"),
        AccountName = GetStr(reader, "AccountName"),
        HeaderKey = GetLong(reader, "HeaderKey"),
        GroupName = GetStr(reader, "GroupName"),
        GroupSortOrder = GetInt(reader, "GroupSortOrder"),
        CategoryName = GetStr(reader, "CategoryName"),
        CategorySortOrder = GetInt(reader, "CategorySortOrder"),
        IsBank = GetBool(reader, "IsBank"),
        Amount = GetDec(reader, "Amount")
    };

    private static async Task<List<CashFlowRow>> QueryBudgetAsync(SqlConnection conn, DateTime from, DateTime to)
    {
        // Mirrors the TVF budget branch: unpivot Amount01..12 to month-start dates within the
        // budget's fiscal year, keep months inside [from, to]. Display sign = negated natural
        // balance sign: DR-type account budget = cash out (negative), CR-type = cash in (positive).
        // Detail-level lines map by account code; header-level lines map by the header's COA code
        // (ac_prefix + ac_number — the same code the PBI dimension exposes as HeaderCode).
        var sql = $@"
;WITH Bud AS (
    SELECT CASE WHEN ISNULL(unpiv.fk_DetailID, '') = '' THEN '' ELSE unpiv.AccountCode END AS AccountCode,
           CASE WHEN ISNULL(unpiv.fk_DetailID, '') = '' THEN unpiv.pk_Key ELSE unpiv.fk_parent END AS HeaderKey,
           CASE WHEN ISNULL(unpiv.fk_DetailID, '') = '' THEN 1 ELSE 0 END AS IsHeaderLevel,
           unpiv.MonthValue,
           unpiv.Amount,
           h.fk_ad_fispe AS FiscalYear
    FROM tbl_AccBudgetDetails
    UNPIVOT (Amount FOR MonthValue IN
        (Amount01,Amount02,Amount03,Amount04,Amount05,Amount06,
         Amount07,Amount08,Amount09,Amount10,Amount11,Amount12)) unpiv
    INNER JOIN tbl_AccBudgetKey h ON unpiv.fk_BudgetCode = h.pk_BudgetCode
    WHERE h.fk_ad_fispe IS NOT NULL
),
BudDated AS (
    SELECT b.AccountCode, b.HeaderKey, b.IsHeaderLevel, b.Amount,
           CONVERT(date, b.FiscalYear + '-' + RIGHT(b.MonthValue, 2) + '-01') AS BudgetMonth
    FROM Bud b
)
SELECT b.AccountCode,
       CASE WHEN b.IsHeaderLevel = 1 THEN ISNULL(c.ac_name, '') ELSE ISNULL(d.da_name, '') END AS AccountName,
       CAST(b.HeaderKey AS bigint)         AS HeaderKey,
       ISNULL(map.GroupName, '')           AS GroupName,
       ISNULL(map.GroupSortOrder, 0)       AS GroupSortOrder,
       ISNULL(map.CategoryName, '')        AS CategoryName,
       ISNULL(map.CategorySortOrder, 0)    AS CategorySortOrder,
       CAST(CASE WHEN ba.pk_ba_code IS NULL THEN 0 ELSE 1 END AS bit) AS IsBank,
       CONVERT(decimal(18,2), 0)           AS Amount,
       CONVERT(decimal(18,2), SUM(
           CASE WHEN ISNULL(c.ac_drcr, d.da_dctype) = 'DR' THEN -1 ELSE 1 END * b.Amount)) AS BudgetAmount
FROM BudDated b
LEFT JOIN tbl_coa c WITH (NOLOCK) ON b.HeaderKey = c.pk_Key AND b.IsHeaderLevel = 1
LEFT JOIN tbl_detailac d WITH (NOLOCK) ON b.AccountCode = d.pk_DetailID AND b.IsHeaderLevel = 0
LEFT JOIN tbl_accbank ba WITH (NOLOCK) ON b.AccountCode = ba.fk_ba_link AND b.IsHeaderLevel = 0
{string.Format(MapApply, "(CASE WHEN b.IsHeaderLevel = 1 THEN ISNULL(c.ac_prefix, '') + CAST(c.ac_number AS nvarchar(10)) ELSE b.AccountCode END)")}
WHERE b.BudgetMonth >= @from AND b.BudgetMonth <= @to
GROUP BY b.AccountCode, b.IsHeaderLevel, d.da_name, c.ac_name, b.HeaderKey,
         map.GroupName, map.GroupSortOrder, map.CategoryName, map.CategorySortOrder,
         ba.pk_ba_code
HAVING SUM(b.Amount) <> 0;";

        var rows = new List<CashFlowRow>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        // Budget months are month-start dates; align @from to its month start so a period starting
        // mid-month still picks up that month's budget (PBI slices by month granularity).
        var fromMonth = new DateTime(from.Year, from.Month, 1);
        cmd.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = fromMonth });
        cmd.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = to.Date });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var r = ReadRow(reader);
            r.BudgetAmount = GetDec(reader, "BudgetAmount");
            r.Amount = 0m;
            rows.Add(r);
        }
        return rows;
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

    private static long GetLong(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return reader.IsDBNull(ord) ? 0L : Convert.ToInt64(reader.GetValue(ord));
    }

    private static bool GetBool(SqlDataReader reader, string col)
    {
        var ord = reader.GetOrdinal(col);
        return !reader.IsDBNull(ord) && Convert.ToBoolean(reader.GetValue(ord));
    }
}
