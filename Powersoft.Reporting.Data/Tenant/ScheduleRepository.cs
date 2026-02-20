using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class ScheduleRepository : IScheduleRepository
{
    private readonly string _connectionString;

    public ScheduleRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> CreateScheduleAsync(ReportSchedule schedule)
    {
        const string sql = @"
            INSERT INTO tbl_ReportSchedule 
                (ReportType, ScheduleName, CreatedBy, RecurrenceType, RecurrenceDay,
                 ScheduleTime, NextRunDate, ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject)
            VALUES 
                (@ReportType, @ScheduleName, @CreatedBy, @RecurrenceType, @RecurrenceDay,
                 @ScheduleTime, @NextRunDate, @ParametersJson, @RecurrenceJson, @ExportFormat, @Recipients, @EmailSubject);
            SELECT SCOPE_IDENTITY();";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@ReportType", schedule.ReportType);
        cmd.Parameters.AddWithValue("@ScheduleName", schedule.ScheduleName);
        cmd.Parameters.AddWithValue("@CreatedBy", schedule.CreatedBy);
        cmd.Parameters.AddWithValue("@RecurrenceType", schedule.RecurrenceType);
        cmd.Parameters.AddWithValue("@RecurrenceDay", (object?)schedule.RecurrenceDay ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScheduleTime", schedule.ScheduleTime);
        cmd.Parameters.AddWithValue("@NextRunDate", (object?)schedule.NextRunDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ParametersJson", (object?)schedule.ParametersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RecurrenceJson", (object?)schedule.RecurrenceJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExportFormat", schedule.ExportFormat);
        cmd.Parameters.AddWithValue("@Recipients", schedule.Recipients);
        cmd.Parameters.AddWithValue("@EmailSubject", (object?)schedule.EmailSubject ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<ReportSchedule>> GetSchedulesForReportAsync(string reportType)
    {
        const string sql = @"
            SELECT pk_ScheduleID, ReportType, ScheduleName, CreatedBy, CreatedDate, IsActive,
                   RecurrenceType, RecurrenceDay, ScheduleTime, NextRunDate, LastRunDate,
                   ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject
            FROM tbl_ReportSchedule
            WHERE ReportType = @ReportType AND IsActive = 1
            ORDER BY CreatedDate DESC";

        var schedules = new List<ReportSchedule>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ReportType", reportType);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    public async Task<ReportSchedule?> GetScheduleByIdAsync(int scheduleId)
    {
        const string sql = @"
            SELECT pk_ScheduleID, ReportType, ScheduleName, CreatedBy, CreatedDate, IsActive,
                   RecurrenceType, RecurrenceDay, ScheduleTime, NextRunDate, LastRunDate,
                   ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject
            FROM tbl_ReportSchedule
            WHERE pk_ScheduleID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", scheduleId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSchedule(reader) : null;
    }

    public async Task<bool> UpdateScheduleAsync(ReportSchedule schedule)
    {
        const string sql = @"
            UPDATE tbl_ReportSchedule 
            SET ScheduleName = @ScheduleName, RecurrenceType = @RecurrenceType,
                RecurrenceDay = @RecurrenceDay, ScheduleTime = @ScheduleTime,
                NextRunDate = @NextRunDate, ParametersJson = @ParametersJson,
                RecurrenceJson = @RecurrenceJson,
                ExportFormat = @ExportFormat, Recipients = @Recipients,
                EmailSubject = @EmailSubject, IsActive = @IsActive,
                ModifiedDate = GETDATE(), ModifiedBy = @ModifiedBy
            WHERE pk_ScheduleID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@Id", schedule.ScheduleId);
        cmd.Parameters.AddWithValue("@ScheduleName", schedule.ScheduleName);
        cmd.Parameters.AddWithValue("@RecurrenceType", schedule.RecurrenceType);
        cmd.Parameters.AddWithValue("@RecurrenceDay", (object?)schedule.RecurrenceDay ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScheduleTime", schedule.ScheduleTime);
        cmd.Parameters.AddWithValue("@NextRunDate", (object?)schedule.NextRunDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ParametersJson", (object?)schedule.ParametersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RecurrenceJson", (object?)schedule.RecurrenceJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExportFormat", schedule.ExportFormat);
        cmd.Parameters.AddWithValue("@Recipients", schedule.Recipients);
        cmd.Parameters.AddWithValue("@EmailSubject", (object?)schedule.EmailSubject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsActive", schedule.IsActive);
        cmd.Parameters.AddWithValue("@ModifiedBy", schedule.CreatedBy);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteScheduleAsync(int scheduleId)
    {
        const string sql = "UPDATE tbl_ReportSchedule SET IsActive = 0 WHERE pk_ScheduleID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", scheduleId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> CountActiveSchedulesForReportAsync(string reportType)
    {
        const string sql = "SELECT COUNT(*) FROM tbl_ReportSchedule WHERE ReportType = @ReportType AND IsActive = 1";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ReportType", reportType);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static ReportSchedule MapSchedule(SqlDataReader reader)
    {
        return new ReportSchedule
        {
            ScheduleId = reader.GetInt32(0),
            ReportType = reader.GetString(1),
            ScheduleName = reader.GetString(2),
            CreatedBy = reader.GetString(3),
            CreatedDate = reader.GetDateTime(4),
            IsActive = reader.GetBoolean(5),
            RecurrenceType = reader.GetString(6),
            RecurrenceDay = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            ScheduleTime = reader.GetTimeSpan(8),
            NextRunDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            LastRunDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            ParametersJson = reader.IsDBNull(11) ? null : reader.GetString(11),
            RecurrenceJson = reader.IsDBNull(12) ? null : reader.GetString(12),
            ExportFormat = reader.GetString(13),
            Recipients = reader.GetString(14),
            EmailSubject = reader.IsDBNull(15) ? null : reader.GetString(15)
        };
    }
}
