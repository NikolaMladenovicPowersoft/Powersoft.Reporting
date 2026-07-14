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
            ORDER BY pk_SeasonID DESC";

        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetSuppliersAsync(string? search = null, int maxResults = 500)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        // tbl_Supplier has no single "SupplierName" column; display name is built the same way
        // as everywhere else in the codebase (PurchasesSales/Catalogue/Chart repos):
        // Company=1 -> LastCompanyName, otherwise FirstName + LastCompanyName.
        var sql = $@"
            SELECT TOP (@MaxResults)
                   pk_SupplierNo AS Id,
                   pk_SupplierNo AS Code,
                   CASE WHEN ISNULL(Company,0) = 1 THEN ISNULL(LastCompanyName,'')
                        ELSE ISNULL(FirstName,'') + ' ' + ISNULL(LastCompanyName,'') END AS Name
            FROM tbl_Supplier
            WHERE (@Search IS NULL
                OR pk_SupplierNo LIKE @Search
                OR ISNULL(LastCompanyName,'') LIKE @Search
                OR ISNULL(FirstName,'') LIKE @Search)
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
        // tbl_Customer has no single "CustomerName" column; mirror the codebase-wide display-name expression.
        var sql = $@"
            SELECT TOP (@MaxResults)
                   pk_CustomerNo AS Id,
                   pk_CustomerNo AS Code,
                   CASE WHEN ISNULL(Company,0) = 1 THEN ISNULL(LastCompanyName,'')
                        ELSE ISNULL(FirstName,'') + ' ' + ISNULL(LastCompanyName,'') END AS Name
            FROM tbl_Customer
            WHERE (@Search IS NULL
                OR pk_CustomerNo LIKE @Search
                OR ISNULL(LastCompanyName,'') LIKE @Search
                OR ISNULL(FirstName,'') LIKE @Search)
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

    public async Task<List<DimensionItem>> GetModelsAsync()
    {
        const string sql = @"
            SELECT CAST(pk_ModelID AS NVARCHAR(20)), ISNULL(ModelCode,''), ISNULL(ModelNamePrimary, ModelCode)
            FROM tbl_Model
            ORDER BY ModelCode";
        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetColoursAsync()
    {
        const string sql = @"
            SELECT CAST(pk_ColourID AS NVARCHAR(20)), ISNULL(ColourCode,''), ISNULL(ColourName, ColourCode)
            FROM tbl_Colour
            ORDER BY ColourCode";
        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetSizesAsync()
    {
        const string sql = @"
            SELECT CAST(pk_SizeID AS NVARCHAR(20)), ISNULL(SizeCode,''), ISNULL(SizeName, SizeCode)
            FROM tbl_Size
            ORDER BY SizeCode";
        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetGroupSizesAsync()
    {
        const string sql = @"
            SELECT CAST(pk_SizeGroupID AS NVARCHAR(20)), ISNULL(SizeGroupCode,''), ISNULL(SizeGroupName, SizeGroupCode)
            FROM tbl_SizeGroup
            ORDER BY SizeGroupCode";
        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetFabricsAsync()
    {
        const string sql = @"
            SELECT CAST(pk_FabricID AS NVARCHAR(20)), ISNULL(FabricCode,''), ISNULL(FabricDescr, FabricCode)
            FROM tbl_Fabric
            ORDER BY FabricCode";
        return await ExecuteListQueryAsync(sql);
    }

    public async Task<List<DimensionItem>> GetAttributeValuesAsync(int attrIndex)
    {
        if (attrIndex < 1 || attrIndex > 6) return new List<DimensionItem>();

        // Attribute id lives on tbl_Item.fk_AttrID{n}; the human-readable code/name live on
        // the denormalised tbl_RelItemAttributes (keyed by pk_ItemID). The previous query read
        // fk_AttrID{n} straight off tbl_RelItemAttributes, where that column does not exist.
        var codeCol = $"r.Attribute{attrIndex}Code";
        var nameCol = $"r.Attribute{attrIndex}Name";
        var sql = $@"
            SELECT DISTINCT
                CAST(i.fk_AttrID{attrIndex} AS NVARCHAR(20)) AS Id,
                ISNULL({codeCol},'') AS Code,
                ISNULL({nameCol}, {codeCol}) AS Name
            FROM tbl_Item i
            JOIN tbl_RelItemAttributes r ON i.pk_ItemID = r.pk_ItemID
            WHERE i.fk_AttrID{attrIndex} IS NOT NULL AND i.fk_AttrID{attrIndex} > 0
            ORDER BY Code";

        return await ExecuteListQueryAsync(sql);
    }

    // Legacy parity: ItemsSelections.ascx.vb (CloudAccounting) reads tbl_Field for
    // TableName='tbl_Item' AND FieldType=3 and uses FieldDesc as the caption for AttrVal1..6
    // — that's how legacy shows "GENDER" / "MATERIAL" instead of "Attribute 1".
    public async Task<Dictionary<int, string>> GetAttributeCaptionsAsync()
    {
        const string sql = @"
            SELECT FieldName, ISNULL(FieldDesc,'') AS FieldDesc
            FROM tbl_Field
            WHERE TableName = 'tbl_Item' AND FieldType = 3
              AND FieldName LIKE 'AttrVal[1-6]'";

        var result = new Dictionary<int, string>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fieldName = reader.GetString(0);
            var desc = reader.GetString(1).Trim();
            // FieldName is 'AttrVal1'..'AttrVal6' (enforced by the LIKE above)
            if (int.TryParse(fieldName.Substring(7), out var idx) && idx >= 1 && idx <= 6
                && desc.Length > 0)
            {
                result[idx] = desc;
            }
        }
        return result;
    }

    // A dimension is "available" only if items actually reference it (not merely that a
    // dimension master table has a seeded row), so non-fashion tenants stay uncluttered.
    // Model/Colour/Size live on tbl_Item; GroupSize/Fabric on tbl_Model; Attributes on
    // tbl_RelItemAttributes. Single round-trip; each EXISTS short-circuits.
    public async Task<FashionDimensionAvailability> GetFashionDimensionAvailabilityAsync()
    {
        const string sql = @"
            SELECT
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Item  WHERE fk_ModelID  > 0) THEN 1 ELSE 0 END AS INT) AS HasModels,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Item  WHERE fk_ColourID > 0) THEN 1 ELSE 0 END AS INT) AS HasColours,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Item  WHERE fk_SizeID   > 0) THEN 1 ELSE 0 END AS INT) AS HasSizes,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Model WHERE fk_SizeGroupID > 0) THEN 1 ELSE 0 END AS INT) AS HasGroupSizes,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Model WHERE fk_FabricID > 0) THEN 1 ELSE 0 END AS INT) AS HasFabrics,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM tbl_Item
                    WHERE fk_AttrID1 > 0 OR fk_AttrID2 > 0 OR fk_AttrID3 > 0
                       OR fk_AttrID4 > 0 OR fk_AttrID5 > 0 OR fk_AttrID6 > 0) THEN 1 ELSE 0 END AS INT) AS HasAttributes";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        var result = new FashionDimensionAvailability();
        if (await reader.ReadAsync())
        {
            result.HasModels = reader.GetInt32(0) == 1;
            result.HasColours = reader.GetInt32(1) == 1;
            result.HasSizes = reader.GetInt32(2) == 1;
            result.HasGroupSizes = reader.GetInt32(3) == 1;
            result.HasFabrics = reader.GetInt32(4) == 1;
            result.HasAttributes = reader.GetInt32(5) == 1;
        }
        return result;
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
