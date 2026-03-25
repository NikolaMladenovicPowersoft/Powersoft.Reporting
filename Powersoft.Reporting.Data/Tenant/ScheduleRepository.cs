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
                 ScheduleTime, NextRunDate, ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject,
                 IncludeAiAnalysis, AiLocale)
            VALUES 
                (@ReportType, @ScheduleName, @CreatedBy, @RecurrenceType, @RecurrenceDay,
                 @ScheduleTime, @NextRunDate, @ParametersJson, @RecurrenceJson, @ExportFormat, @Recipients, @EmailSubject,
                 @IncludeAiAnalysis, @AiLocale);
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
        cmd.Parameters.AddWithValue("@IncludeAiAnalysis", schedule.IncludeAiAnalysis);
        cmd.Parameters.AddWithValue("@AiLocale", schedule.AiLocale ?? "el");

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<ReportSchedule>> GetSchedulesForReportAsync(string reportType)
    {
        const string sql = @"
            SELECT pk_ScheduleID, ReportType, ScheduleName, CreatedBy, CreatedDate, IsActive,
                   RecurrenceType, RecurrenceDay, ScheduleTime, NextRunDate, LastRunDate,
                   ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject,
                   ISNULL(IncludeAiAnalysis, 0) AS IncludeAiAnalysis, ISNULL(AiLocale, 'el') AS AiLocale
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
                   ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject,
                   ISNULL(IncludeAiAnalysis, 0) AS IncludeAiAnalysis, ISNULL(AiLocale, 'el') AS AiLocale
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
                IncludeAiAnalysis = @IncludeAiAnalysis, AiLocale = @AiLocale,
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
        cmd.Parameters.AddWithValue("@IncludeAiAnalysis", schedule.IncludeAiAnalysis);
        cmd.Parameters.AddWithValue("@AiLocale", schedule.AiLocale ?? "el");
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

    public async Task<List<ReportSchedule>> GetDueSchedulesAsync(DateTime asOfUtc)
    {
        var schedules = new List<ReportSchedule>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tran = (SqlTransaction)await conn.BeginTransactionAsync();

        try
        {
            using var lockCmd = new SqlCommand(
                "EXEC @rc = sp_getapplock @Resource=N'ReportScheduler', @LockMode=N'Exclusive', @LockTimeout=0", conn, tran);
            var rcParam = lockCmd.Parameters.Add("@rc", System.Data.SqlDbType.Int);
            rcParam.Direction = System.Data.ParameterDirection.Output;
            await lockCmd.ExecuteNonQueryAsync();

            int lockResult = (int)rcParam.Value;
            if (lockResult < 0)
                return schedules;

            const string sql = @"
                SELECT pk_ScheduleID, ReportType, ScheduleName, CreatedBy, CreatedDate, IsActive,
                       RecurrenceType, RecurrenceDay, ScheduleTime, NextRunDate, LastRunDate,
                       ParametersJson, RecurrenceJson, ExportFormat, Recipients, EmailSubject,
                       ISNULL(IncludeAiAnalysis, 0) AS IncludeAiAnalysis, ISNULL(AiLocale, 'el') AS AiLocale
                FROM tbl_ReportSchedule
                WHERE IsActive = 1
                  AND NextRunDate IS NOT NULL
                  AND NextRunDate <= @AsOf
                ORDER BY NextRunDate";

            using var cmd = new SqlCommand(sql, conn, tran);
            cmd.Parameters.AddWithValue("@AsOf", asOfUtc);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                schedules.Add(MapSchedule(reader));
            }
            reader.Close();

            if (schedules.Count > 0)
            {
                var ids = string.Join(",", schedules.Select(s => s.ScheduleId));
                using var claimCmd = new SqlCommand(
                    $"UPDATE tbl_ReportSchedule SET NextRunDate = NULL WHERE pk_ScheduleID IN ({ids})", conn, tran);
                await claimCmd.ExecuteNonQueryAsync();
            }

            await tran.CommitAsync();
        }
        catch
        {
            try { await tran.RollbackAsync(); } catch { }
            throw;
        }

        return schedules;
    }

    public async Task UpdateAfterExecutionAsync(int scheduleId, DateTime lastRunDate, DateTime? nextRunDate, bool deactivate)
    {
        const string sql = @"
            UPDATE tbl_ReportSchedule
            SET LastRunDate = @LastRunDate,
                NextRunDate = @NextRunDate,
                IsActive = CASE WHEN @Deactivate = 1 THEN 0 ELSE IsActive END,
                ModifiedDate = GETDATE()
            WHERE pk_ScheduleID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", scheduleId);
        cmd.Parameters.AddWithValue("@LastRunDate", lastRunDate);
        cmd.Parameters.AddWithValue("@NextRunDate", (object?)nextRunDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Deactivate", deactivate ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertScheduleLogAsync(ScheduleLog log)
    {
        const string sql = @"
            INSERT INTO tbl_ReportScheduleLog
                (fk_ScheduleID, RunDate, Status, RowsGenerated, FileSizeBytes, ErrorMessage, DurationMs)
            VALUES
                (@ScheduleId, @RunDate, @Status, @RowsGenerated, @FileSizeBytes, @ErrorMessage, @DurationMs);
            SELECT SCOPE_IDENTITY();";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ScheduleId", log.ScheduleId);
        cmd.Parameters.AddWithValue("@RunDate", log.RunDate);
        cmd.Parameters.AddWithValue("@Status", log.Status);
        cmd.Parameters.AddWithValue("@RowsGenerated", (object?)log.RowsGenerated ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FileSizeBytes", (object?)log.FileSizeBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)log.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", (object?)log.DurationMs ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<ScheduleLogEntry>> GetScheduleLogsAsync(int? scheduleId = null, int top = 100)
    {
        var sql = @"
            SELECT TOP (@Top)
                   l.pk_LogID, l.fk_ScheduleID, s.ScheduleName, s.ReportType,
                   l.RunDate, l.Status, l.RowsGenerated, l.FileSizeBytes, l.ErrorMessage, l.DurationMs
            FROM tbl_ReportScheduleLog l
            INNER JOIN tbl_ReportSchedule s ON s.pk_ScheduleID = l.fk_ScheduleID"
            + (scheduleId.HasValue ? " WHERE l.fk_ScheduleID = @ScheduleId" : "")
            + " ORDER BY l.RunDate DESC";

        var entries = new List<ScheduleLogEntry>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Top", top);
        if (scheduleId.HasValue)
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new ScheduleLogEntry
            {
                LogId = reader.GetInt32(0),
                ScheduleId = reader.GetInt32(1),
                ScheduleName = reader.GetString(2),
                ReportType = reader.GetString(3),
                RunDate = reader.GetDateTime(4),
                Status = reader.GetString(5),
                RowsGenerated = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                FileSizeBytes = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                DurationMs = reader.IsDBNull(9) ? null : reader.GetInt32(9)
            });
        }

        return entries;
    }

    // ==================== Email Templates ====================

    public async Task<List<EmailTemplate>> GetEmailTemplatesAsync(string? reportType = null)
    {
        var sql = @"SELECT pk_TemplateID, TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, IsActive, CreatedBy, CreatedDate
                    FROM tbl_ReportEmailTemplate
                    WHERE IsActive = 1"
            + (reportType != null ? " AND (ReportType = @ReportType OR ReportType IS NULL OR ReportType = '')" : "")
            + " ORDER BY IsDefault DESC, TemplateName";

        var templates = new List<EmailTemplate>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            templates.Add(MapEmailTemplate(reader));

        return templates;
    }

    public async Task<EmailTemplate?> GetEmailTemplateByIdAsync(int templateId)
    {
        const string sql = @"SELECT pk_TemplateID, TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, IsActive, CreatedBy, CreatedDate
                             FROM tbl_ReportEmailTemplate WHERE pk_TemplateID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", templateId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapEmailTemplate(reader) : null;
    }

    public async Task<EmailTemplate?> GetDefaultEmailTemplateAsync(string? reportType = null)
    {
        var sql = @"SELECT TOP 1 pk_TemplateID, TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, IsActive, CreatedBy, CreatedDate
                    FROM tbl_ReportEmailTemplate
                    WHERE IsActive = 1 AND IsDefault = 1"
            + (reportType != null ? " AND (ReportType = @ReportType OR ReportType IS NULL OR ReportType = '')" : "")
            + " ORDER BY ReportType DESC";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapEmailTemplate(reader) : null;
    }

    public async Task<int> CreateEmailTemplateAsync(EmailTemplate template)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        if (template.IsDefault)
            await ClearDefaultFlagAsync(conn, null, template.ReportType);

        const string sql = @"INSERT INTO tbl_ReportEmailTemplate
            (TemplateName, ReportType, EmailSubject, EmailBodyHtml, IsDefault, CreatedBy)
            VALUES (@Name, @ReportType, @Subject, @Body, @IsDefault, @CreatedBy);
            SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name", template.TemplateName);
        cmd.Parameters.AddWithValue("@ReportType", (object?)template.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Subject", template.EmailSubject);
        cmd.Parameters.AddWithValue("@Body", template.EmailBodyHtml);
        cmd.Parameters.AddWithValue("@IsDefault", template.IsDefault);
        cmd.Parameters.AddWithValue("@CreatedBy", template.CreatedBy);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateEmailTemplateAsync(EmailTemplate template)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        if (template.IsDefault)
            await ClearDefaultFlagAsync(conn, template.TemplateId, template.ReportType);

        const string sql = @"UPDATE tbl_ReportEmailTemplate
            SET TemplateName = @Name, ReportType = @ReportType, EmailSubject = @Subject,
                EmailBodyHtml = @Body, IsDefault = @IsDefault, ModifiedDate = GETDATE(), ModifiedBy = @ModifiedBy
            WHERE pk_TemplateID = @Id";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", template.TemplateId);
        cmd.Parameters.AddWithValue("@Name", template.TemplateName);
        cmd.Parameters.AddWithValue("@ReportType", (object?)template.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Subject", template.EmailSubject);
        cmd.Parameters.AddWithValue("@Body", template.EmailBodyHtml);
        cmd.Parameters.AddWithValue("@IsDefault", template.IsDefault);
        cmd.Parameters.AddWithValue("@ModifiedBy", template.CreatedBy);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task ClearDefaultFlagAsync(SqlConnection conn, int? excludeId, string? reportType)
    {
        var sql = "UPDATE tbl_ReportEmailTemplate SET IsDefault = 0 WHERE IsDefault = 1 AND IsActive = 1";

        if (reportType != null)
            sql += " AND ReportType = @ReportType";
        else
            sql += " AND (ReportType IS NULL OR ReportType = '')";

        if (excludeId.HasValue)
            sql += " AND pk_TemplateID <> @ExcludeId";

        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);
        if (excludeId.HasValue)
            cmd.Parameters.AddWithValue("@ExcludeId", excludeId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteEmailTemplateAsync(int templateId)
    {
        const string sql = @"UPDATE tbl_ReportEmailTemplate SET IsActive = 0, ModifiedDate = GETDATE() WHERE pk_TemplateID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", templateId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static EmailTemplate MapEmailTemplate(SqlDataReader reader)
    {
        return new EmailTemplate
        {
            TemplateId = reader.GetInt32(0),
            TemplateName = reader.GetString(1),
            ReportType = reader.IsDBNull(2) ? null : reader.GetString(2),
            EmailSubject = reader.GetString(3),
            EmailBodyHtml = reader.GetString(4),
            IsDefault = reader.GetBoolean(5),
            IsActive = reader.GetBoolean(6),
            CreatedBy = reader.GetString(7),
            CreatedDate = reader.GetDateTime(8)
        };
    }

    private static ReportSchedule MapSchedule(SqlDataReader reader)
    {
        var schedule = new ReportSchedule
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

        if (reader.FieldCount > 16)
        {
            schedule.IncludeAiAnalysis = !reader.IsDBNull(16) && reader.GetBoolean(16);
            schedule.AiLocale = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : "el";
        }

        return schedule;
    }

    // ==================== AI Prompt Templates ====================

    public async Task<List<AiPromptTemplate>> GetAiPromptTemplatesAsync(string? reportType = null)
    {
        var sql = @"SELECT pk_TemplateID, TemplateName, ReportType, SystemPrompt, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy
                    FROM tbl_AiPromptTemplate WHERE IsActive = 1"
            + (reportType != null ? " AND (ReportType = @ReportType OR ReportType IS NULL OR ReportType = '')" : "")
            + " ORDER BY IsDefault DESC, TemplateName";

        var list = new List<AiPromptTemplate>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapAiPromptTemplate(reader));
        return list;
    }

    public async Task<AiPromptTemplate?> GetAiPromptTemplateByIdAsync(int templateId)
    {
        const string sql = @"SELECT pk_TemplateID, TemplateName, ReportType, SystemPrompt, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy
                             FROM tbl_AiPromptTemplate WHERE pk_TemplateID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", templateId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapAiPromptTemplate(reader) : null;
    }

    public async Task<AiPromptTemplate?> GetDefaultAiPromptTemplateAsync(string? reportType = null)
    {
        var sql = @"SELECT TOP 1 pk_TemplateID, TemplateName, ReportType, SystemPrompt, IsDefault, IsActive, CreatedBy, CreatedDate, ModifiedDate, ModifiedBy
                    FROM tbl_AiPromptTemplate WHERE IsActive = 1 AND IsDefault = 1"
            + (reportType != null ? " AND (ReportType = @ReportType OR ReportType IS NULL OR ReportType = '')" : "")
            + " ORDER BY ReportType DESC";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapAiPromptTemplate(reader) : null;
    }

    public async Task<int> CreateAiPromptTemplateAsync(AiPromptTemplate template)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        if (template.IsDefault)
            await ClearAiPromptDefaultFlagAsync(conn, null, template.ReportType);

        const string sql = @"INSERT INTO tbl_AiPromptTemplate
            (TemplateName, ReportType, SystemPrompt, IsDefault, CreatedBy)
            VALUES (@Name, @ReportType, @SystemPrompt, @IsDefault, @CreatedBy);
            SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name", template.TemplateName);
        cmd.Parameters.AddWithValue("@ReportType", (object?)template.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SystemPrompt", template.SystemPrompt);
        cmd.Parameters.AddWithValue("@IsDefault", template.IsDefault);
        cmd.Parameters.AddWithValue("@CreatedBy", template.CreatedBy);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateAiPromptTemplateAsync(AiPromptTemplate template)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        if (template.IsDefault)
            await ClearAiPromptDefaultFlagAsync(conn, template.TemplateId, template.ReportType);

        const string sql = @"UPDATE tbl_AiPromptTemplate
            SET TemplateName = @Name, ReportType = @ReportType, SystemPrompt = @SystemPrompt,
                IsDefault = @IsDefault, ModifiedDate = GETDATE(), ModifiedBy = @ModifiedBy
            WHERE pk_TemplateID = @Id";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", template.TemplateId);
        cmd.Parameters.AddWithValue("@Name", template.TemplateName);
        cmd.Parameters.AddWithValue("@ReportType", (object?)template.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SystemPrompt", template.SystemPrompt);
        cmd.Parameters.AddWithValue("@IsDefault", template.IsDefault);
        cmd.Parameters.AddWithValue("@ModifiedBy", template.CreatedBy);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAiPromptTemplateAsync(int templateId)
    {
        const string sql = "UPDATE tbl_AiPromptTemplate SET IsActive = 0, ModifiedDate = GETDATE() WHERE pk_TemplateID = @Id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", templateId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task ClearAiPromptDefaultFlagAsync(SqlConnection conn, int? excludeId, string? reportType)
    {
        var sql = "UPDATE tbl_AiPromptTemplate SET IsDefault = 0 WHERE IsDefault = 1 AND IsActive = 1";
        if (reportType != null)
            sql += " AND ReportType = @ReportType";
        else
            sql += " AND (ReportType IS NULL OR ReportType = '')";
        if (excludeId.HasValue)
            sql += " AND pk_TemplateID <> @ExcludeId";

        using var cmd = new SqlCommand(sql, conn);
        if (reportType != null)
            cmd.Parameters.AddWithValue("@ReportType", reportType);
        if (excludeId.HasValue)
            cmd.Parameters.AddWithValue("@ExcludeId", excludeId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AiPromptTemplate MapAiPromptTemplate(SqlDataReader reader)
    {
        return new AiPromptTemplate
        {
            TemplateId = reader.GetInt32(0),
            TemplateName = reader.GetString(1),
            ReportType = reader.IsDBNull(2) ? null : reader.GetString(2),
            SystemPrompt = reader.GetString(3),
            IsDefault = reader.GetBoolean(4),
            IsActive = reader.GetBoolean(5),
            CreatedBy = reader.GetString(6),
            CreatedDate = reader.GetDateTime(7),
            ModifiedDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            ModifiedBy = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }
}
