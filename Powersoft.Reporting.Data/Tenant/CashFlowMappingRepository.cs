using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// CRUD over dboReportsAI.tbl_CashFlowMapping for the Cash Flow Mapping admin page.
/// The report engine (CashFlowRepository) reads the same table with most-specific-range-wins
/// resolution: greatest CodeFrom, then GroupSortOrder, CategorySortOrder, pk_MappingID —
/// ResolveAccountAsync below mirrors that ORDER BY exactly so the admin "test account" tool
/// always shows what the report will actually do.
/// </summary>
public class CashFlowMappingRepository : ICashFlowMappingRepository
{
    private readonly string _connectionString;

    public CashFlowMappingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<CashFlowMappingEntry>> GetAllAsync()
    {
        const string sql = @"
SELECT pk_MappingID, GroupName, GroupSortOrder, CategoryName, CategorySortOrder, CodeFrom, CodeTo
FROM dboReportsAI.tbl_CashFlowMapping
ORDER BY GroupSortOrder, CategorySortOrder, CodeFrom, pk_MappingID";

        var result = new List<CashFlowMappingEntry>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new CashFlowMappingEntry
            {
                PkMappingID = reader.GetInt32(0),
                GroupName = reader.GetString(1),
                GroupSortOrder = reader.GetInt32(2),
                CategoryName = reader.GetString(3),
                CategorySortOrder = reader.GetInt32(4),
                CodeFrom = reader.GetString(5),
                CodeTo = reader.GetString(6)
            });
        }
        return result;
    }

    public async Task<int> InsertAsync(CashFlowMappingEntry entry)
    {
        const string sql = @"
INSERT INTO dboReportsAI.tbl_CashFlowMapping
    (GroupName, GroupSortOrder, CategoryName, CategorySortOrder, CodeFrom, CodeTo)
OUTPUT INSERTED.pk_MappingID
VALUES (@GroupName, @GroupSortOrder, @CategoryName, @CategorySortOrder, @CodeFrom, @CodeTo)";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, entry);
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task<bool> UpdateAsync(CashFlowMappingEntry entry)
    {
        const string sql = @"
UPDATE dboReportsAI.tbl_CashFlowMapping
SET GroupName = @GroupName, GroupSortOrder = @GroupSortOrder,
    CategoryName = @CategoryName, CategorySortOrder = @CategorySortOrder,
    CodeFrom = @CodeFrom, CodeTo = @CodeTo
WHERE pk_MappingID = @PkMappingID";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, entry);
        cmd.Parameters.AddWithValue("@PkMappingID", entry.PkMappingID);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> DeleteAsync(int pkMappingID)
    {
        const string sql = "DELETE FROM dboReportsAI.tbl_CashFlowMapping WHERE pk_MappingID = @Id";
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", pkMappingID);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<CashFlowMappingResolution> ResolveAccountAsync(string accountCode)
    {
        // Same resolution as CashFlowRepository.MapApply: inclusive string range match,
        // most specific range wins (greatest CodeFrom, then sort orders, then pk).
        const string sql = @"
SELECT COUNT(*) FROM dboReportsAI.tbl_CashFlowMapping
WHERE @Code >= CodeFrom AND @Code <= CodeTo;

SELECT TOP 1 pk_MappingID, GroupName, CategoryName, CodeFrom, CodeTo
FROM dboReportsAI.tbl_CashFlowMapping
WHERE @Code >= CodeFrom AND @Code <= CodeTo
ORDER BY CodeFrom DESC, GroupSortOrder, CategorySortOrder, pk_MappingID";

        var result = new CashFlowMappingResolution();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Code", (accountCode ?? "").Trim());
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
            result.MatchCount = reader.GetInt32(0);

        if (await reader.NextResultAsync() && await reader.ReadAsync())
        {
            result.Matched = true;
            result.PkMappingID = reader.GetInt32(0);
            result.GroupName = reader.GetString(1);
            result.CategoryName = reader.GetString(2);
            result.CodeFrom = reader.GetString(3);
            result.CodeTo = reader.GetString(4);
        }
        return result;
    }

    // Same cash-relevance slice as CashFlowRepository.ActualsSql: an account is "active" when it
    // appears on a leg of a bank-touching transaction that is not OB/YE — i.e. the report could
    // actually show it. Coverage sorts these first because unmapped active accounts are the ones
    // that end up in "(Unassigned)" on the statement.
    private const string AccountsCte = @"
;WITH CashTx AS (
    SELECT DISTINCT t.fk_tt_number
    FROM tbl_payments t WITH (NOLOCK)
    INNER JOIN tbl_accbank b WITH (NOLOCK) ON t.fk_tt_accode = b.fk_ba_link
),
Active AS (
    SELECT DISTINCT t.fk_tt_accode AS Code
    FROM tbl_payments t WITH (NOLOCK)
    INNER JOIN CashTx c ON t.fk_tt_number = c.fk_tt_number
    WHERE t.fk_tt_type NOT IN ('OB', 'YE')
),
Acc AS (
    SELECT d.pk_detailid AS Code,
           ISNULL(d.da_name, '') AS Name,
           CASE WHEN a.Code IS NULL THEN 0 ELSE 1 END AS HasCashActivity,
           CASE WHEN EXISTS (
               SELECT 1 FROM dboReportsAI.tbl_CashFlowMapping m WITH (NOLOCK)
               WHERE d.pk_detailid >= m.CodeFrom AND d.pk_detailid <= m.CodeTo
           ) THEN 1 ELSE 0 END AS Mapped
    FROM tbl_detailac d WITH (NOLOCK)
    LEFT JOIN Active a ON a.Code = d.pk_detailid
)";

    public async Task<CashFlowMappingCoverage> GetCoverageAsync(int maxUnassigned = 200)
    {
        var sql = AccountsCte + @"
SELECT COUNT(*)                                              AS TotalAccounts,
       ISNULL(SUM(Mapped), 0)                                AS MappedAccounts,
       ISNULL(SUM(HasCashActivity), 0)                       AS ActiveAccounts,
       ISNULL(SUM(CASE WHEN HasCashActivity = 1 THEN Mapped ELSE 0 END), 0) AS ActiveMapped
FROM Acc;
" + AccountsCte + @"
SELECT TOP (@MaxRows) Code, Name, HasCashActivity
FROM Acc
WHERE Mapped = 0
ORDER BY HasCashActivity DESC, Code;";

        var result = new CashFlowMappingCoverage();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@MaxRows", maxUnassigned + 1);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            result.TotalAccounts = reader.GetInt32(0);
            result.MappedAccounts = reader.GetInt32(1);
            result.ActiveAccounts = reader.GetInt32(2);
            result.ActiveMappedAccounts = reader.GetInt32(3);
        }

        if (await reader.NextResultAsync())
        {
            while (await reader.ReadAsync())
            {
                result.UnassignedAccounts.Add(new CashFlowMappingAccount
                {
                    Code = reader.GetValue(0).ToString() ?? "",
                    Name = reader.GetString(1),
                    HasCashActivity = reader.GetInt32(2) == 1
                });
            }
        }

        if (result.UnassignedAccounts.Count > maxUnassigned)
        {
            result.UnassignedTruncated = true;
            result.UnassignedAccounts.RemoveAt(result.UnassignedAccounts.Count - 1);
        }
        return result;
    }

    public async Task<CashFlowMappingRangePreview> PreviewRangeAsync(
        string codeFrom, string codeTo, int excludeId, int maxSample = 10)
    {
        var sql = AccountsCte + @"
SELECT COUNT(*) FROM Acc WHERE Code >= @From AND Code <= @To;
" + AccountsCte + @"
SELECT TOP (@MaxSample) Code, Name, HasCashActivity
FROM Acc
WHERE Code >= @From AND Code <= @To
ORDER BY HasCashActivity DESC, Code;

SELECT pk_MappingID, GroupName, GroupSortOrder, CategoryName, CategorySortOrder, CodeFrom, CodeTo
FROM dboReportsAI.tbl_CashFlowMapping WITH (NOLOCK)
WHERE CodeFrom <= @To AND CodeTo >= @From AND pk_MappingID <> @ExcludeId
ORDER BY CodeFrom DESC, GroupSortOrder, CategorySortOrder, pk_MappingID;";

        var result = new CashFlowMappingRangePreview();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@From", (codeFrom ?? "").Trim());
        cmd.Parameters.AddWithValue("@To", (codeTo ?? "").Trim());
        cmd.Parameters.AddWithValue("@ExcludeId", excludeId);
        cmd.Parameters.AddWithValue("@MaxSample", maxSample);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
            result.MatchCount = reader.GetInt32(0);

        if (await reader.NextResultAsync())
        {
            while (await reader.ReadAsync())
            {
                result.SampleAccounts.Add(new CashFlowMappingAccount
                {
                    Code = reader.GetValue(0).ToString() ?? "",
                    Name = reader.GetString(1),
                    HasCashActivity = reader.GetInt32(2) == 1
                });
            }
        }

        if (await reader.NextResultAsync())
        {
            while (await reader.ReadAsync())
            {
                result.OverlappingRanges.Add(new CashFlowMappingEntry
                {
                    PkMappingID = reader.GetInt32(0),
                    GroupName = reader.GetString(1),
                    GroupSortOrder = reader.GetInt32(2),
                    CategoryName = reader.GetString(3),
                    CategorySortOrder = reader.GetInt32(4),
                    CodeFrom = reader.GetString(5),
                    CodeTo = reader.GetString(6)
                });
            }
        }
        return result;
    }

    public async Task<int> ResetToDefaultsAsync()
    {
        // Single batch so a failed insert can't leave the table empty.
        var sql = $@"
BEGIN TRANSACTION;
DELETE FROM dboReportsAI.tbl_CashFlowMapping;
INSERT INTO dboReportsAI.tbl_CashFlowMapping
    (GroupName, GroupSortOrder, CategoryName, CategorySortOrder, CodeFrom, CodeTo)
VALUES
{SchemaMigrationService.CashFlowMappingSeedValues};
COMMIT TRANSACTION;
SELECT COUNT(*) FROM dboReportsAI.tbl_CashFlowMapping;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        var count = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    private static void AddParams(SqlCommand cmd, CashFlowMappingEntry entry)
    {
        cmd.Parameters.AddWithValue("@GroupName", entry.GroupName);
        cmd.Parameters.AddWithValue("@GroupSortOrder", entry.GroupSortOrder);
        cmd.Parameters.AddWithValue("@CategoryName", entry.CategoryName);
        cmd.Parameters.AddWithValue("@CategorySortOrder", entry.CategorySortOrder);
        cmd.Parameters.AddWithValue("@CodeFrom", entry.CodeFrom);
        cmd.Parameters.AddWithValue("@CodeTo", entry.CodeTo);
    }
}
