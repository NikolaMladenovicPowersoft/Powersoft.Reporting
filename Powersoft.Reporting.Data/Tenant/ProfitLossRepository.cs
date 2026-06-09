using System.Data;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// Profit &amp; Loss data access. Ported 1:1 from legacy Powersoft.CloudAccounting:
///   - Powersoft.CloudReports\ProfitAndLoss.vb (group mapping, sign convention, stock injection)
///   - Powersoft.CloudQueries\WQR.vb:5326 ProfitAndLoss (balance SQL, da_nominal=0, exclude 'YE')
///   - WQR.vb GetControlHeaderRow / IsAncestor / GetAllCOAChildNodes (control-header resolution +
///     recursive COA descent, leaf-only descendants)
///
/// Sources: tbl_payments (journal lines), tbl_detailac (detail accounts), tbl_coa (COA tree),
/// tbl_acontrol (control-header → COA pk_Key mapping via cr_ascode).
///
/// Four control headers drive the four P&amp;L groups:
///   SALESHEA -> Sales (1), COSTOSHEA -> Cost of Sales (2), INCHEA -> Income (3), EXPHEA -> Expenses (4).
/// </summary>
public class ProfitLossRepository : IProfitLossRepository
{
    private readonly string _connectionString;

    // Legacy WQR control-header codes (tbl_acontrol.pk_cr_varco).
    private const string SalesCode = "SALESHEA";
    private const string CostCode = "COSTOSHEA";
    private const string IncomeCode = "INCHEA";
    private const string ExpensesCode = "EXPHEA";

    public ProfitLossRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(List<ProfitLossRow> rows, string? configError)> GenerateAsync(ProfitLossFilter filter)
    {
        var suppressSet = new HashSet<int>(SplitCsvInt(filter.SuppressedHeaders));

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1) Resolve the four control header keys + names and verify they are configured.
        var (salesKey, costKey, incKey, expKey,
             salesName, costName, incName, expName, configError) = await ResolveControlHeadersAsync(conn);

        if (configError != null)
            return (new List<ProfitLossRow>(), configError);

        // 2) Current period balances.
        var current = await GetBalancesAsync(conn, filter.DateFrom, filter.DateTo,
            salesKey, costKey, incKey, expKey);

        // 3) Prior-year balances (same range shifted back one year), only when comparison requested.
        var prior = filter.CompareToLastYear
            ? await GetBalancesAsync(conn, filter.PriorDateFrom, filter.PriorDateTo,
                salesKey, costKey, incKey, expKey)
            : new Dictionary<string, BalanceRec>();

        var groupNames = new Dictionary<ProfitLossGroup, string>
        {
            [ProfitLossGroup.Sales] = salesName,
            [ProfitLossGroup.CostOfSales] = costName,
            [ProfitLossGroup.Income] = incName,
            [ProfitLossGroup.Expenses] = expName
        };

        var rows = new List<ProfitLossRow>();
        foreach (var rec in current.Values)
        {
            var group = (ProfitLossGroup)rec.Grp;
            decimal bal = NormalizeBalance(rec.Balance, rec.DcType, group);
            decimal priorBal = 0m;
            if (prior.TryGetValue(rec.AccountCode, out var pr) && pr.Grp == rec.Grp)
                priorBal = NormalizeBalance(pr.Balance, pr.DcType, group);

            rows.Add(new ProfitLossRow
            {
                Group = group,
                GroupName = groupNames[group],
                AccountCode = rec.AccountCode,
                AccountName = rec.AccountName,
                HeaderKey = rec.FkParent,
                HeaderName = rec.HeaderName,
                PrefNo = BuildPrefNo(rec.AcPrefix, rec.AcNumber, rec.AccountCode),
                Balance = bal,
                PriorBalance = priorBal,
                Suppressed = !filter.HeaderLevel && suppressSet.Contains(rec.FkParent)
            });
        }

        // 4) Manual opening/closing stock injection into Cost of Sales (legacy ProfitAndLoss.vb 123-173).
        //    Comparison column gets no prior stock (legacy comparison is a chart with no stock input).
        if (filter.OpeningStockValue != 0)
        {
            decimal bal = NormalizeStock(filter.OpeningStockValue, isOpening: true);
            rows.Add(BuildStockRow("OPENING STOCK (USER)", bal, costName));
        }
        if (filter.ClosingStockValue != 0)
        {
            decimal bal = NormalizeStock(filter.ClosingStockValue, isOpening: false);
            rows.Add(BuildStockRow("CLOSING STOCK (USER)", bal, costName));
        }

        // 5) Header-level aggregation (legacy chkHeaderLevel): collapse accounts into their header.
        if (filter.HeaderLevel)
            rows = AggregateToHeaderLevel(rows, groupNames);

        // 6) Sort: group asc, prefno asc, account code asc (legacy DataView sort).
        rows = rows
            .OrderBy(r => (int)r.Group)
            .ThenBy(r => r.PrefNo, StringComparer.Ordinal)
            .ThenBy(r => r.AccountCode, StringComparer.Ordinal)
            .ToList();

        return (rows, null);
    }

    public async Task<List<ProfitLossHeaderOption>> GetHeadersAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var (salesKey, costKey, incKey, expKey, _, _, _, _, configError) =
            await ResolveControlHeadersAsync(conn);

        var list = new List<ProfitLossHeaderOption>();
        if (configError != null)
            return list;

        // Leaf COA headers that descend from one of the four control headers AND own detail accounts.
        const string sql = @"
;WITH Tree AS (
    SELECT c.pk_Key
    FROM tbl_COA c
    WHERE c.pk_Key IN (@sales, @cost, @inc, @exp)
    UNION ALL
    SELECT child.pk_Key
    FROM tbl_COA child
    INNER JOIN Tree t ON child.fk_parent = t.pk_Key
)
SELECT c.pk_Key,
       LTRIM(RTRIM(ISNULL(c.ac_prefix,''))) + CAST(c.ac_number AS varchar(10)) AS ac_code,
       c.ac_name
FROM tbl_COA c
INNER JOIN Tree t ON t.pk_Key = c.pk_Key
WHERE EXISTS (SELECT 1 FROM tbl_DetailAc d WHERE d.fk_parent = c.pk_Key)
ORDER BY c.ac_prefix, c.ac_number
OPTION (MAXRECURSION 100);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@sales", SqlDbType.Int) { Value = salesKey });
        cmd.Parameters.Add(new SqlParameter("@cost", SqlDbType.Int) { Value = costKey });
        cmd.Parameters.Add(new SqlParameter("@inc", SqlDbType.Int) { Value = incKey });
        cmd.Parameters.Add(new SqlParameter("@exp", SqlDbType.Int) { Value = expKey });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ProfitLossHeaderOption
            {
                Key = GetInt(reader, "pk_Key"),
                Code = GetStr(reader, "ac_code"),
                Name = GetStr(reader, "ac_name")
            });
        }
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<(int sales, int cost, int inc, int exp,
        string salesName, string costName, string incName, string expName, string? error)>
        ResolveControlHeadersAsync(SqlConnection conn)
    {
        // tbl_acontrol.cr_ascode holds the COA pk_Key (as text) of the control header.
        const string sql = @"
SELECT ac.pk_cr_varco,
       TRY_CONVERT(int, ac.cr_ascode) AS coa_key,
       c.ac_name
FROM tbl_acontrol ac
LEFT JOIN tbl_COA c ON c.pk_Key = TRY_CONVERT(int, ac.cr_ascode)
WHERE ac.pk_cr_varco IN (@s, @c, @i, @e);";

        int sales = 0, cost = 0, inc = 0, exp = 0;
        string salesName = "SALES", costName = "COST OF SALES", incName = "INCOME", expName = "EXPENSES";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.NVarChar, 10) { Value = SalesCode });
        cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.NVarChar, 10) { Value = CostCode });
        cmd.Parameters.Add(new SqlParameter("@i", SqlDbType.NVarChar, 10) { Value = IncomeCode });
        cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.NVarChar, 10) { Value = ExpensesCode });

        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var code = GetStr(reader, "pk_cr_varco").Trim();
                int key = GetInt(reader, "coa_key");
                var name = GetStr(reader, "ac_name");
                switch (code)
                {
                    case SalesCode: sales = key; if (name.Length > 0) salesName = name; break;
                    case CostCode: cost = key; if (name.Length > 0) costName = name; break;
                    case IncomeCode: inc = key; if (name.Length > 0) incName = name; break;
                    case ExpensesCode: exp = key; if (name.Length > 0) expName = name; break;
                }
            }
        }

        var missing = new List<string>();
        if (sales == 0) missing.Add("SALES CONTROL HEADER not defined.");
        if (exp == 0) missing.Add("EXPENSES CONTROL HEADER not defined.");
        if (cost == 0) missing.Add("COST OF SALES CONTROL HEADER not defined.");
        if (inc == 0) missing.Add("INCOME CONTROL HEADER not defined.");

        string? error = missing.Count > 0 ? string.Join(Environment.NewLine, missing) : null;
        return (sales, cost, inc, exp, salesName, costName, incName, expName, error);
    }

    private static async Task<Dictionary<string, BalanceRec>> GetBalancesAsync(
        SqlConnection conn, DateTime from, DateTime to,
        int salesKey, int costKey, int incKey, int expKey)
    {
        // Recursive COA tree tagged with group; leaf-only descendants (mirrors GetAllCOAChildNodes).
        // Balance query mirrors WQR.ProfitAndLoss: da_nominal=0, exclude 'YE', signed sum.
        const string sql = @"
;WITH Tree AS (
    SELECT c.pk_Key, m.grp
    FROM tbl_COA c
    INNER JOIN (VALUES (@sales,1),(@cost,2),(@inc,3),(@exp,4)) m(k, grp) ON c.pk_Key = m.k
    UNION ALL
    SELECT child.pk_Key, t.grp
    FROM tbl_COA child
    INNER JOIN Tree t ON child.fk_parent = t.pk_Key
),
CoaGroup AS (
    -- leaf nodes only: a detail account hangs off a leaf COA header (legacy IsAncestor uses leaf set)
    SELECT t.pk_Key, t.grp
    FROM Tree t
    WHERE NOT EXISTS (SELECT 1 FROM tbl_COA x WHERE x.fk_parent = t.pk_Key)
)
SELECT b.pk_detailid, b.da_name, b.da_dctype, b.fk_parent,
       c.ac_name, c.ac_prefix, c.ac_number, g.grp,
       SUM(CASE WHEN b.da_dctype = a.tt_drcr THEN a.pamount ELSE -a.pamount END) AS balance
FROM tbl_detailac b
INNER JOIN tbl_payments a ON b.pk_detailid = a.fk_tt_accode
INNER JOIN CoaGroup g ON g.pk_Key = b.fk_parent
LEFT JOIN tbl_coa c ON b.fk_parent = c.pk_key
WHERE b.da_nominal = 0
  AND CONVERT(date, a.pdate) >= CONVERT(date, @from)
  AND CONVERT(date, a.pdate) <= CONVERT(date, @to)
  AND a.fk_tt_type <> 'YE'
GROUP BY b.pk_detailid, b.da_name, b.da_dctype, b.fk_parent,
         c.ac_name, c.ac_prefix, c.ac_number, g.grp
HAVING SUM(CASE WHEN b.da_dctype = a.tt_drcr THEN a.pamount ELSE -a.pamount END) IS NOT NULL
OPTION (MAXRECURSION 100);";

        var result = new Dictionary<string, BalanceRec>(StringComparer.Ordinal);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@sales", SqlDbType.Int) { Value = salesKey });
        cmd.Parameters.Add(new SqlParameter("@cost", SqlDbType.Int) { Value = costKey });
        cmd.Parameters.Add(new SqlParameter("@inc", SqlDbType.Int) { Value = incKey });
        cmd.Parameters.Add(new SqlParameter("@exp", SqlDbType.Int) { Value = expKey });
        cmd.Parameters.Add(new SqlParameter("@from", SqlDbType.Date) { Value = from.Date });
        cmd.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = to.Date });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = GetStr(reader, "pk_detailid");
            result[code] = new BalanceRec
            {
                AccountCode = code,
                AccountName = GetStr(reader, "da_name"),
                DcType = GetStr(reader, "da_dctype"),
                FkParent = GetInt(reader, "fk_parent"),
                HeaderName = GetStr(reader, "ac_name"),
                AcPrefix = GetStr(reader, "ac_prefix"),
                AcNumber = GetStr(reader, "ac_number"),
                Grp = GetInt(reader, "grp"),
                Balance = GetDec(reader, "balance")
            };
        }
        return result;
    }

    /// <summary>
    /// Sign-normalizes a raw signed balance into the legacy nRealBalance (positive = favourable
    /// contribution to profit), mirroring ProfitAndLoss.vb lines 78-107.
    /// </summary>
    private static decimal NormalizeBalance(decimal rawBalance, string dcType, ProfitLossGroup group)
    {
        // cBalType: for CR-type accounts, balance>=0 => CR else DR; for others, balance<0 => CR else DR.
        string balType;
        if (dcType == "CR")
            balType = rawBalance >= 0 ? "CR" : "DR";
        else
            balType = rawBalance < 0 ? "CR" : "DR";

        decimal abs = Math.Abs(rawBalance);

        return group switch
        {
            // Sales & Income are credit-favourable: positive when CR.
            ProfitLossGroup.Sales or ProfitLossGroup.Income => balType == "DR" ? -abs : abs,
            // Cost of Sales & Expenses are debit-favourable: positive when DR.
            _ => balType == "DR" ? abs : -abs
        };
    }

    /// <summary>Opening/closing stock sign convention (legacy ProfitAndLoss.vb 123-173, Cost of Sales group).</summary>
    private static decimal NormalizeStock(decimal value, bool isOpening)
    {
        decimal abs = Math.Abs(value);
        string balType;
        if (isOpening)
            balType = value < 0 ? "CR" : "DR";   // cAccType "DR"
        else
            balType = value < 0 ? "DR" : "CR";   // cAccType "CR"

        // Cost of Sales group: DR positive.
        return balType == "DR" ? abs : -abs;
    }

    private static ProfitLossRow BuildStockRow(string name, decimal balance, string costName) => new()
    {
        Group = ProfitLossGroup.CostOfSales,
        GroupName = costName,
        AccountCode = "",
        AccountName = name,
        HeaderKey = 0,
        HeaderName = costName,
        PrefNo = "",
        Balance = balance,
        PriorBalance = 0m,
        Suppressed = false
    };

    private static List<ProfitLossRow> AggregateToHeaderLevel(
        List<ProfitLossRow> rows, Dictionary<ProfitLossGroup, string> groupNames)
    {
        // Collapse per header (within each group). Synthetic stock rows (HeaderKey 0) stay separate.
        return rows
            .GroupBy(r => new { r.Group, r.HeaderKey, r.HeaderName })
            .Select(g => new ProfitLossRow
            {
                Group = g.Key.Group,
                GroupName = groupNames[g.Key.Group],
                AccountCode = "",
                AccountName = g.Key.HeaderName,
                HeaderKey = g.Key.HeaderKey,
                HeaderName = g.Key.HeaderName,
                PrefNo = g.First().PrefNo,
                Balance = g.Sum(x => x.Balance),
                PriorBalance = g.Sum(x => x.PriorBalance),
                Suppressed = false
            })
            .ToList();
    }

    private static string BuildPrefNo(string prefix, string number, string accountCode) =>
        prefix.Trim() + number.Trim() + " - " + accountCode;

    private static List<int> SplitCsvInt(string? csv)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    private sealed class BalanceRec
    {
        public string AccountCode = "";
        public string AccountName = "";
        public string DcType = "";
        public int FkParent;
        public string HeaderName = "";
        public string AcPrefix = "";
        public string AcNumber = "";
        public int Grp;
        public decimal Balance;
    }
}
