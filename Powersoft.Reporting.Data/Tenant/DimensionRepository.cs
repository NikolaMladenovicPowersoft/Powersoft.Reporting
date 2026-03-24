using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class DimensionRepository : IDimensionRepository
{
    private readonly string _connectionString;

    public DimensionRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<DimensionItem>> GetCategoriesAsync()
    {
        const string sql = @"
            SELECT CAST(pk_CategoryID AS NVARCHAR(20)), CategoryCode, CategoryDescr
            FROM tbl_ItemCategory
            ORDER BY CategoryCode";

        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetDepartmentsAsync()
    {
        const string sql = @"
            SELECT CAST(pk_DepartmentID AS NVARCHAR(20)), DepartmentCode, DepartmentDescr
            FROM tbl_ItemDepartment
            ORDER BY DepartmentCode";

        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetBrandsAsync()
    {
        const string sql = @"
            SELECT CAST(pk_BrandID AS NVARCHAR(20)), BrandCode, BrandDesc
            FROM tbl_Brands
            ORDER BY BrandCode";

        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetSeasonsAsync()
    {
        const string sql = @"
            SELECT CAST(pk_SeasonID AS NVARCHAR(20)), SeasonCode, SeasonDesc
            FROM tbl_Season
            ORDER BY SeasonCode";

        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetSuppliersAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults) pk_SupplierNo, pk_SupplierNo, SupplierName
            FROM tbl_Supplier
            WHERE (@Search IS NULL
                OR pk_SupplierNo LIKE @Search
                OR ISNULL(SupplierName,'') LIKE @Search)
            ORDER BY pk_SupplierNo";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    public async Task<List<DimensionItem>> GetCustomersAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults) pk_CustomerNo, pk_CustomerNo, ISNULL(CustomerName, pk_CustomerNo)
            FROM tbl_Customer
            WHERE (@Search IS NULL
                OR pk_CustomerNo LIKE @Search
                OR ISNULL(CustomerName,'') LIKE @Search)
            ORDER BY pk_CustomerNo";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    private async Task<List<DimensionItem>> ExecuteListQueryAsync(string sql)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    private static async Task<List<DimensionItem>> ReadResultsAsync(SqlCommand cmd)
    {
        var results = new List<DimensionItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DimensionItem
            {
                Id = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                Code = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim()
            });
        }
        return results;
    }
}
