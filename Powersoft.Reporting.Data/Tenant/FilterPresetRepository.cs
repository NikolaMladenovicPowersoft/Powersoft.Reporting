using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class FilterPresetRepository : IFilterPresetRepository
{
    private const string Schema = "dboReportsAI";

    public async Task<List<FilterPreset>> GetPresetsAsync(string connectionString, string userCode, string? reportType)
    {
        var sql = $@"SELECT pk_PresetID, PresetName, ReportType, FilterJson, CreatedBy, CreatedDate, ModifiedDate, IsShared
                     FROM {Schema}.tbl_FilterPreset
                     WHERE (CreatedBy = @User OR IsShared = 1)
                       AND (@ReportType IS NULL OR ReportType IS NULL OR ReportType = @ReportType)
                     ORDER BY PresetName";

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@User", userCode);
        cmd.Parameters.AddWithValue("@ReportType", (object?)reportType ?? DBNull.Value);

        var list = new List<FilterPreset>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new FilterPreset
            {
                PresetId = rdr.GetInt32(0),
                PresetName = rdr.GetString(1),
                ReportType = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                FilterJson = rdr.GetString(3),
                CreatedBy = rdr.GetString(4),
                CreatedDate = rdr.GetDateTime(5),
                ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
                IsShared = rdr.GetBoolean(7)
            });
        }
        return list;
    }

    public async Task<FilterPreset?> GetPresetByIdAsync(string connectionString, int presetId)
    {
        var sql = $@"SELECT pk_PresetID, PresetName, ReportType, FilterJson, CreatedBy, CreatedDate, ModifiedDate, IsShared
                     FROM {Schema}.tbl_FilterPreset WHERE pk_PresetID = @Id";

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", presetId);

        using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;
        return new FilterPreset
        {
            PresetId = rdr.GetInt32(0),
            PresetName = rdr.GetString(1),
            ReportType = rdr.IsDBNull(2) ? null : rdr.GetString(2),
            FilterJson = rdr.GetString(3),
            CreatedBy = rdr.GetString(4),
            CreatedDate = rdr.GetDateTime(5),
            ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
            IsShared = rdr.GetBoolean(7)
        };
    }

    public async Task<int> SavePresetAsync(string connectionString, FilterPreset preset)
    {
        string sql;
        if (preset.PresetId > 0)
        {
            sql = $@"UPDATE {Schema}.tbl_FilterPreset
                     SET PresetName = @Name, ReportType = @ReportType, FilterJson = @Json,
                         IsShared = @Shared, ModifiedDate = GETDATE()
                     WHERE pk_PresetID = @Id AND CreatedBy = @User;
                     SELECT @Id;";
        }
        else
        {
            sql = $@"INSERT INTO {Schema}.tbl_FilterPreset (PresetName, ReportType, FilterJson, CreatedBy, IsShared)
                     VALUES (@Name, @ReportType, @Json, @User, @Shared);
                     SELECT SCOPE_IDENTITY();";
        }

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", preset.PresetId);
        cmd.Parameters.AddWithValue("@Name", preset.PresetName);
        cmd.Parameters.AddWithValue("@ReportType", (object?)preset.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Json", preset.FilterJson);
        cmd.Parameters.AddWithValue("@User", preset.CreatedBy);
        cmd.Parameters.AddWithValue("@Shared", preset.IsShared);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> DeletePresetAsync(string connectionString, int presetId, string userCode)
    {
        var sql = $"DELETE FROM {Schema}.tbl_FilterPreset WHERE pk_PresetID = @Id AND CreatedBy = @User";
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", presetId);
        cmd.Parameters.AddWithValue("@User", userCode);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
