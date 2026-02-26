using System.Diagnostics;
using System.Text.Json;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Helpers;

namespace Powersoft.Reporting.Web.Services;

/// <summary>
/// Executes due scheduled reports across all tenant databases.
/// Called by an external trigger (cron job, Azure Timer, or manual HTTP endpoint).
/// Flow: get all DBs → per DB get due schedules → generate report → email → update → log.
/// </summary>
public class ScheduleExecutionService
{
    private readonly ICentralRepository _centralRepository;
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ScheduleExecutionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ScheduleExecutionService(
        ICentralRepository centralRepository,
        ITenantRepositoryFactory repositoryFactory,
        IEmailSender emailSender,
        ILogger<ScheduleExecutionService> logger)
    {
        _centralRepository = centralRepository;
        _repositoryFactory = repositoryFactory;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<ScheduleRunSummary> RunAllDueSchedulesAsync(CancellationToken ct = default)
    {
        var summary = new ScheduleRunSummary();
        var now = DateTime.Now;

        _logger.LogInformation("Schedule runner started at {Time}", now);

        List<Database> databases;
        try
        {
            databases = await GetAllActiveDatabasesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load databases from psCentral");
            summary.Errors.Add($"Failed to load databases: {ex.Message}");
            return summary;
        }

        _logger.LogInformation("Found {Count} active databases to check", databases.Count);

        foreach (var db in databases)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var connString = ConnectionStringBuilder.Build(db);
                await ProcessDatabaseSchedulesAsync(db, connString, now, summary, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process schedules for database {DB}", db.DBFriendlyName);
                summary.Errors.Add($"DB {db.DBFriendlyName}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Schedule runner completed. Processed: {Processed}, Succeeded: {Succeeded}, Failed: {Failed}",
            summary.Processed, summary.Succeeded, summary.Failed);

        return summary;
    }

    private async Task<List<Database>> GetAllActiveDatabasesAsync()
    {
        var allDatabases = new List<Database>();
        var companies = await _centralRepository.GetActiveCompaniesAsync();

        foreach (var company in companies)
        {
            var dbs = await _centralRepository.GetActiveDatabasesForCompanyAsync(company.CompanyCode);
            allDatabases.AddRange(dbs);
        }

        return allDatabases;
    }

    private async Task ProcessDatabaseSchedulesAsync(
        Database db, string connString, DateTime now,
        ScheduleRunSummary summary, CancellationToken ct)
    {
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(connString);
            var ini = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode, ModuleConstants.IniHeaderDbSettings, "ALL");
            var dbSettings = DatabaseSettings.FromDictionary(ini);

            if (!dbSettings.SchedulerEnabled)
            {
                _logger.LogInformation("Scheduler disabled for DB {DB} — skipping", db.DBFriendlyName);
                return;
            }
        }
        catch
        {
            // Settings table may not exist yet — continue with defaults (scheduler enabled)
        }

        var scheduleRepo = _repositoryFactory.CreateScheduleRepository(connString);

        List<ReportSchedule> dueSchedules;
        try
        {
            dueSchedules = await scheduleRepo.GetDueSchedulesAsync(now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not query schedules for DB {DB} — table may not exist yet", db.DBFriendlyName);
            return;
        }

        if (dueSchedules.Count == 0) return;

        _logger.LogInformation("DB {DB}: {Count} due schedule(s)", db.DBFriendlyName, dueSchedules.Count);

        foreach (var schedule in dueSchedules)
        {
            if (ct.IsCancellationRequested) break;
            await ExecuteSingleScheduleAsync(schedule, db, connString, scheduleRepo, now, summary, ct);
        }
    }

    private async Task ExecuteSingleScheduleAsync(
        ReportSchedule schedule, Database db, string connString,
        IScheduleRepository scheduleRepo, DateTime now,
        ScheduleRunSummary summary, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var log = new ScheduleLog
        {
            ScheduleId = schedule.ScheduleId,
            RunDate = now
        };

        try
        {
            _logger.LogInformation(
                "Executing schedule {Id} '{Name}' (type: {Type}) for DB {DB}",
                schedule.ScheduleId, schedule.ScheduleName, schedule.ReportType, db.DBFriendlyName);

            var (rows, totals, filter, fileBytes, fileName, contentType) =
                await GenerateReportAsync(schedule, connString);

            log.RowsGenerated = rows.Count;
            log.FileSizeBytes = fileBytes.Length;

            await SendReportEmailAsync(schedule, db, fileBytes, fileName, contentType, rows.Count, filter, ct);

            var isOnce = string.Equals(schedule.RecurrenceType, "Once", StringComparison.OrdinalIgnoreCase);
            DateTime? nextRun = isOnce
                ? null
                : RecurrenceNextRunCalculator.GetNextRun(schedule.RecurrenceJson, now)
                  ?? CalculateFallbackNextRun(schedule, now);

            await scheduleRepo.UpdateAfterExecutionAsync(
                schedule.ScheduleId, now, nextRun, deactivate: isOnce);

            log.Status = ScheduleLogStatus.Success;
            sw.Stop();
            log.DurationMs = (int)sw.ElapsedMilliseconds;

            summary.Succeeded++;
            _logger.LogInformation(
                "Schedule {Id} completed in {Ms}ms — {Rows} rows, {Size} bytes",
                schedule.ScheduleId, log.DurationMs, log.RowsGenerated, log.FileSizeBytes);
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.Status = ScheduleLogStatus.Failed;
            log.DurationMs = (int)sw.ElapsedMilliseconds;
            log.ErrorMessage = ex.Message;

            summary.Failed++;
            _logger.LogError(ex,
                "Schedule {Id} '{Name}' failed after {Ms}ms",
                schedule.ScheduleId, schedule.ScheduleName, log.DurationMs);
        }
        finally
        {
            summary.Processed++;
            try
            {
                await scheduleRepo.InsertScheduleLogAsync(log);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to insert schedule log for schedule {Id}", schedule.ScheduleId);
            }
        }
    }

    private async Task<(
        List<AverageBasketRow> rows,
        ReportGrandTotals? totals,
        ReportFilter filter,
        byte[] fileBytes,
        string fileName,
        string contentType)> GenerateReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new ReportFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Breakdown = parameters.Breakdown,
            GroupBy = parameters.GroupBy,
            SecondaryGroupBy = parameters.SecondaryGroupBy,
            IncludeVat = parameters.IncludeVat,
            CompareLastYear = parameters.CompareLastYear,
            StoreCodes = parameters.StoreCodes ?? new(),
            ItemIds = parameters.ItemIds ?? new(),
            SortColumn = parameters.SortColumn,
            SortDirection = parameters.SortDirection,
            PageSize = int.MaxValue
        };

        var repo = _repositoryFactory.CreateAverageBasketRepository(connString);
        var result = await repo.GetAverageBasketDataAsync(filter);

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                var pdfService = new PdfExportService();
                fileBytes = pdfService.GenerateAverageBasketPdf(result.Items, result.GrandTotals, filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;

            default:
                var excelService = new ExcelExportService();
                fileBytes = excelService.GenerateAverageBasketExcel(result.Items, result.GrandTotals, filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        return (result.Items, result.GrandTotals, filter, fileBytes, fileName, contentType);
    }

    private async Task SendReportEmailAsync(
        ReportSchedule schedule, Database db,
        byte[] fileBytes, string fileName, string contentType,
        int rowCount, ReportFilter filter, CancellationToken ct)
    {
        var recipients = schedule.Recipients
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (recipients.Length == 0)
        {
            _logger.LogWarning("Schedule {Id} has no recipients — skipping email", schedule.ScheduleId);
            return;
        }

        var subject = !string.IsNullOrWhiteSpace(schedule.EmailSubject)
            ? schedule.EmailSubject
            : $"Scheduled Report: {schedule.ScheduleName} — {db.DBFriendlyName}";

        var htmlBody = BuildEmailHtml(schedule, db, rowCount, filter, fileName);
        var textBody = BuildEmailText(schedule, db, rowCount, filter);

        var attachments = new[]
        {
            new EmailAttachment
            {
                FileName = fileName,
                Content = fileBytes,
                ContentType = contentType
            }
        };

        foreach (var recipient in recipients)
        {
            try
            {
                await _emailSender.SendAsync(recipient, subject, htmlBody, textBody, attachments, ct);
                _logger.LogInformation("Email sent to {Recipient} for schedule {Id}", recipient, schedule.ScheduleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipient} for schedule {Id}",
                    recipient, schedule.ScheduleId);
            }
        }
    }

    private static ScheduleParameters DeserializeParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ScheduleParameters();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var p = new ScheduleParameters();

            if (root.TryGetProperty("breakdown", out var bd))
                p.Breakdown = ParseEnum<BreakdownType>(bd);
            else if (root.TryGetProperty("Breakdown", out var bd2))
                p.Breakdown = ParseEnum<BreakdownType>(bd2);

            if (root.TryGetProperty("groupBy", out var gb))
                p.GroupBy = ParseEnum<GroupByType>(gb);
            else if (root.TryGetProperty("GroupBy", out var gb2))
                p.GroupBy = ParseEnum<GroupByType>(gb2);

            if (root.TryGetProperty("secondaryGroupBy", out var sgb))
                p.SecondaryGroupBy = ParseEnum<GroupByType>(sgb);
            else if (root.TryGetProperty("SecondaryGroupBy", out var sgb2))
                p.SecondaryGroupBy = ParseEnum<GroupByType>(sgb2);

            if (root.TryGetProperty("includeVat", out var iv))
                p.IncludeVat = iv.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("compareLastYear", out var cly))
                p.CompareLastYear = cly.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("sortColumn", out var sc))
                p.SortColumn = sc.GetString() ?? "Period";
            if (root.TryGetProperty("sortDirection", out var sd))
                p.SortDirection = sd.GetString() ?? "ASC";

            // storeCodes: can be array ["S1","S2"] or comma-separated string "S1,S2" or empty string
            if (root.TryGetProperty("storeCodes", out var stc) || root.TryGetProperty("StoreCodes", out stc))
                p.StoreCodes = ParseStringList(stc);

            // itemIds: can be array [1,2] or comma-separated string "1,2" or empty string
            if (root.TryGetProperty("itemIds", out var iid) || root.TryGetProperty("ItemIds", out iid))
                p.ItemIds = ParseIntList(iid);

            // reportDateRange (frontend format) or DateRange (code format)
            if (root.TryGetProperty("reportDateRange", out var rdr) || root.TryGetProperty("DateRange", out rdr))
            {
                if (rdr.ValueKind == JsonValueKind.Object)
                {
                    p.DateRange = new ReportDateRangeOption();
                    if (rdr.TryGetProperty("type", out var t) || rdr.TryGetProperty("Type", out t))
                    {
                        var typeStr = t.GetString();
                        if (Enum.TryParse<ReportDateRangeType>(typeStr, true, out var parsed))
                            p.DateRange.Type = parsed;
                    }
                    if (rdr.TryGetProperty("value", out var v) || rdr.TryGetProperty("Value", out v))
                        p.DateRange.Value = v.TryGetInt32(out var vi) ? vi : 7;
                    if (rdr.TryGetProperty("dateFrom", out var df) || rdr.TryGetProperty("DateFrom", out df))
                        p.DateRange.DateFrom = df.GetString();
                    if (rdr.TryGetProperty("dateTo", out var dt) || rdr.TryGetProperty("DateTo", out dt))
                        p.DateRange.DateTo = dt.GetString();
                }
            }

            return p;
        }
        catch
        {
            return new ScheduleParameters();
        }
    }

    private static T ParseEnum<T>(JsonElement el) where T : struct, Enum
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var num))
            return Enum.IsDefined(typeof(T), num) ? (T)(object)num : default;
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (int.TryParse(s, out var n) && Enum.IsDefined(typeof(T), n))
                return (T)(object)n;
            if (Enum.TryParse<T>(s, true, out var parsed))
                return parsed;
        }
        return default;
    }

    private static List<string>? ParseStringList(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s != "").ToList();
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        return null;
    }

    private static List<int>? ParseIntList(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToList();
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => int.TryParse(v, out _)).Select(v => int.Parse(v)).ToList();
        }
        return null;
    }

    private DateTime? CalculateFallbackNextRun(ReportSchedule schedule, DateTime fromWhen)
    {
        var time = schedule.ScheduleTime;
        var todayAtTime = fromWhen.Date.Add(time);

        return schedule.RecurrenceType?.ToLowerInvariant() switch
        {
            "daily" => todayAtTime > fromWhen ? todayAtTime : todayAtTime.AddDays(1),
            "weekly" => todayAtTime.AddDays(7),
            "monthly" => todayAtTime.AddMonths(1),
            _ => todayAtTime.AddDays(1)
        };
    }

    private static string BuildEmailHtml(
        ReportSchedule schedule, Database db,
        int rowCount, ReportFilter filter, string fileName)
    {
        return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px;'>
    <h2 style='color: #2563eb;'>Scheduled Report: {schedule.ScheduleName}</h2>
    <table style='border-collapse: collapse; width: 100%; margin: 16px 0;'>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Database</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'><strong>{db.DBFriendlyName}</strong></td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Report Type</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{schedule.ReportType}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Period</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Rows</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{rowCount}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Format</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{schedule.ExportFormat}</td></tr>
    </table>
    <p style='color: #6b7280; font-size: 13px;'>
        File: {fileName}<br/>
        Generated: {DateTime.Now:yyyy-MM-dd HH:mm}
    </p>
    <p style='color: #9ca3af; font-size: 11px; margin-top: 24px;'>
        This is an automated report from Powersoft Reporting Engine.<br/>
        To modify or stop this schedule, log in to the Reporting dashboard.
    </p>
</div>";
    }

    private static string BuildEmailText(
        ReportSchedule schedule, Database db,
        int rowCount, ReportFilter filter)
    {
        return $@"Scheduled Report: {schedule.ScheduleName}
Database: {db.DBFriendlyName}
Report Type: {schedule.ReportType}
Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}
Rows: {rowCount}
Format: {schedule.ExportFormat}
Generated: {DateTime.Now:yyyy-MM-dd HH:mm}

This is an automated report from Powersoft Reporting Engine.";
    }
}

public class ScheduleRunSummary
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}
