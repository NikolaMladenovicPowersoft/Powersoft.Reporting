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

    public async Task<List<DimensionItem>> GetAgentsAsync(string? search = null, int maxResults = 500)
    {
        // Agents are people working for the company (pk_SystemNo in tbl_Agent).
        // Name format matches original repPowerReportCatalogue: FirstName + ' ' + LastName.
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults)
                   CAST(pk_SystemNo AS NVARCHAR(20)) AS Id,
                   CAST(pk_SystemNo AS NVARCHAR(20)) AS Code,
                   LTRIM(RTRIM(ISNULL(FirstName,'') + ' ' + ISNULL(LastName,''))) AS Name
            FROM tbl_Agent
            WHERE (@Search IS NULL
                OR CAST(pk_SystemNo AS NVARCHAR(20)) LIKE @Search
                OR ISNULL(FirstName,'') LIKE @Search
                OR ISNULL(LastName,'') LIKE @Search)
            ORDER BY FirstName, LastName";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    public async Task<List<DimensionItem>> GetPostalCodesAsync(string? search = null, int maxResults = 500)
    {
        // Postal codes are DISTINCT values from tbl_Customer.PostalCode.
        // Mirrors original repPowerReportCatalogue.aspx.vb:3791-3792 (AND e.PostalCode IN (...)).
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults)
                   LTRIM(RTRIM(PostalCode)) AS Id,
                   LTRIM(RTRIM(PostalCode)) AS Code,
                   LTRIM(RTRIM(PostalCode)) AS Name
            FROM (
                SELECT DISTINCT PostalCode
                FROM tbl_Customer
                WHERE PostalCode IS NOT NULL
                  AND LTRIM(RTRIM(PostalCode)) <> ''
                  AND (@Search IS NULL OR PostalCode LIKE @Search)
            ) AS q
            ORDER BY PostalCode";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    /// <summary>
    /// Payment types from tbl_paymtype. Mirrors legacy CC.AddSelection("pk_ptcode","ptdesc","tbl_paymtype",...).
    /// Small lookup table — no search needed.
    /// </summary>
    public async Task<List<DimensionItem>> GetPaymentTypesAsync()
    {
        const string sql = @"
            SELECT LTRIM(RTRIM(pk_ptcode)) AS Id,
                   LTRIM(RTRIM(pk_ptcode)) AS Code,
                   ISNULL(ptdesc, pk_ptcode) AS Name
            FROM tbl_paymtype
            ORDER BY pk_ptcode";

        return await ExecuteListQueryAsync(sql);
    }

    /// <summary>
    /// Z Reports from tbl_ZReport. Filter by search (Z report number).
    /// Mirrors legacy CC.AddSelection("pk_ZReport","pk_ZReport","tbl_ZReport",...).
    /// </summary>
    public async Task<List<DimensionItem>> GetZReportsAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults)
                   LTRIM(RTRIM(CAST(pk_ZReport AS NVARCHAR(50)))) AS Id,
                   LTRIM(RTRIM(CAST(pk_ZReport AS NVARCHAR(50)))) AS Code,
                   LTRIM(RTRIM(CAST(pk_ZReport AS NVARCHAR(50)))) AS Name
            FROM tbl_ZReport
            WHERE pk_ZReport IS NOT NULL
              AND (@Search IS NULL OR CAST(pk_ZReport AS NVARCHAR(50)) LIKE @Search)
            ORDER BY pk_ZReport DESC";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    /// <summary>
    /// Towns — DISTINCT values from tbl_Customer.Town.
    /// Mirrors legacy repPowerReportCatalogue.aspx.vb:3795 (AND e.Town IN (...)).
    /// </summary>
    public async Task<List<DimensionItem>> GetTownsAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults)
                   LTRIM(RTRIM(Town)) AS Id,
                   LTRIM(RTRIM(Town)) AS Code,
                   LTRIM(RTRIM(Town)) AS Name
            FROM (
                SELECT DISTINCT Town
                FROM tbl_Customer
                WHERE Town IS NOT NULL
                  AND LTRIM(RTRIM(Town)) <> ''
                  AND (@Search IS NULL OR Town LIKE @Search)
            ) AS q
            ORDER BY Town";

        var searchTerm = hasSearch ? $"%{search!.Trim()}%" : null;

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await conn.OpenAsync();
        return await ReadResultsAsync(cmd);
    }

    /// <summary>
    /// Users — pk_UserCode + UserDesc from tenant tbl_User. Filterable by code or description.
    /// </summary>
    public async Task<List<DimensionItem>> GetUsersAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var sql = $@"
            SELECT TOP (@MaxResults)
                   LTRIM(RTRIM(pk_UserCode)) AS Id,
                   LTRIM(RTRIM(pk_UserCode)) AS Code,
                   LTRIM(RTRIM(ISNULL(UserDesc, pk_UserCode))) AS Name
            FROM tbl_User
            WHERE pk_UserCode IS NOT NULL
              AND (@Search IS NULL
                   OR pk_UserCode LIKE @Search
                   OR ISNULL(UserDesc,'') LIKE @Search)
            ORDER BY pk_UserCode";

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
