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
