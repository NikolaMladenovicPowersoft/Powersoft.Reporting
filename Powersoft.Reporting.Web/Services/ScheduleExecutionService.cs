using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.Services.AI;
using Powersoft.Reporting.Web.Services.Storage;

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
    private readonly IReportStorageService _storageService;
    private readonly ReportAnalyzerFactory _analyzerFactory;
    private readonly ILogger<ScheduleExecutionService> _logger;
    private readonly string? _psCentralConnString;

    private const int MaxCsvBytesForAi = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ScheduleExecutionService(
        ICentralRepository centralRepository,
        ITenantRepositoryFactory repositoryFactory,
        IEmailSender emailSender,
        IReportStorageService storageService,
        ReportAnalyzerFactory analyzerFactory,
        ILogger<ScheduleExecutionService> logger,
        IConfiguration configuration)
    {
        _centralRepository = centralRepository;
        _repositoryFactory = repositoryFactory;
        _emailSender = emailSender;
        _storageService = storageService;
        _analyzerFactory = analyzerFactory;
        _logger = logger;
        var raw = configuration.GetConnectionString("PSCentral");
        _psCentralConnString = !string.IsNullOrEmpty(raw) ? Cryptography.DecryptPasswordInConnectionString(raw) : null;
    }

    public async Task<ScheduleRunSummary> RunAllDueSchedulesAsync(CancellationToken ct = default)
    {
        var summary = new ScheduleRunSummary();
        var now = DateTime.Now;

        _logger.LogInformation("Schedule runner started at {Time}", now);

        try
        {
            var sysSettings = await _centralRepository.GetSystemSettingsAsync("RE_");
            var systemCfg = SystemSettings.FromDictionary(sysSettings);
            if (!systemCfg.SchedulerMasterEnabled)
            {
                _logger.LogInformation("Scheduler master switch is OFF — aborting run");
                return summary;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read system settings — continuing with defaults");
        }

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
                var connString = !string.IsNullOrEmpty(_psCentralConnString)
                    ? ConnectionStringBuilder.BuildFromReference(db, _psCentralConnString)
                    : ConnectionStringBuilder.Build(db);
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

            var (rowCount, fileBytes, fileName, contentType, period) =
                await GenerateReportAsync(schedule, connString);

            log.RowsGenerated = rowCount;
            log.FileSizeBytes = fileBytes.Length;

            // Upload to cold storage (S3 / DigitalOcean Spaces) if configured
            string? storageKey = null;
            if (_storageService.IsConfigured)
            {
                try
                {
                    storageKey = await _storageService.UploadAsync(fileName, fileBytes, contentType, ct);
                }
                catch (Exception stEx)
                {
                    _logger.LogWarning(stEx, "Failed to upload report to S3 for schedule {Id}", schedule.ScheduleId);
                }
            }

            ReportAnalysis? aiAnalysis = null;
            _logger.LogInformation(
                "Schedule {Id} AI check: IncludeAiAnalysis={IncAi}, AnalyzerConfigured={Cfg}, RowCount={Rows}, AiLocale={Loc}",
                schedule.ScheduleId, schedule.IncludeAiAnalysis, _analyzerFactory.IsConfigured, rowCount, schedule.AiLocale);

            if (schedule.IncludeAiAnalysis && _analyzerFactory.IsConfigured && rowCount > 0)
            {
                aiAnalysis = await RunAiAnalysisSafe(schedule, connString, ct);
            }
            else if (schedule.IncludeAiAnalysis && !_analyzerFactory.IsConfigured)
            {
                _logger.LogWarning("Schedule {Id}: AI analysis requested but analyzer is not configured (no API key)", schedule.ScheduleId);
            }

            await SendReportEmailAsync(schedule, db, fileBytes, fileName, contentType, rowCount, period, storageKey, aiAnalysis, ct);

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

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateReportAsync(ReportSchedule schedule, string connString)
    {
        var reportType = schedule.ReportType ?? ReportTypeConstants.AverageBasket;

        if (string.Equals(reportType, ReportTypeConstants.PurchasesSales, StringComparison.OrdinalIgnoreCase))
            return await GeneratePurchasesSalesReportAsync(schedule, connString);

        return await GenerateAverageBasketReportAsync(schedule, connString);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateAverageBasketReportAsync(ReportSchedule schedule, string connString)
    {
        _logger.LogInformation("Schedule {Id} raw ParametersJson: {Json}", schedule.ScheduleId, schedule.ParametersJson ?? "(null)");

        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        _logger.LogInformation(
            "Schedule {Id} AB report: DateRange type={Type}, resolved={From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            schedule.ScheduleId, parameters.DateRange?.Type, dateFrom, dateTo);

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
                fileBytes = new PdfExportService().GenerateAverageBasketPdf(result.Items, result.GrandTotals, filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateAverageBasketCsv(result.Items, result.GrandTotals, filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateAverageBasketExcel(result.Items, result.GrandTotals, filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (result.Items.Count, fileBytes, fileName, contentType, period);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GeneratePurchasesSalesReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new PurchasesSalesFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportMode = parameters.ReportMode,
            PrimaryGroup = parameters.PrimaryGroup,
            SecondaryGroup = parameters.SecondaryGroup,
            ThirdGroup = parameters.ThirdGroup,
            IncludeVat = parameters.IncludeVat,
            ShowProfit = parameters.ShowProfit,
            ShowStock = parameters.ShowStock,
            StoreCodes = parameters.StoreCodes ?? new(),
            ItemIds = parameters.ItemIds ?? new(),
            SortColumn = parameters.SortColumn ?? "ItemCode",
            SortDirection = parameters.SortDirection ?? "ASC",
            PageSize = int.MaxValue
        };

        var repo = _repositoryFactory.CreatePurchasesSalesRepository(connString);
        var result = await repo.GetPurchasesSalesDataAsync(filter);

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GeneratePurchasesSalesPdf(result.Items, result.PsTotals, filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GeneratePurchasesSalesCsv(result.Items, result.PsTotals, filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GeneratePurchasesSalesExcel(result.Items, result.PsTotals, filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (result.Items.Count, fileBytes, fileName, contentType, period);
    }

    private async Task<ReportAnalysis?> RunAiAnalysisSafe(
        ReportSchedule schedule, string connString, CancellationToken ct)
    {
        try
        {
            var reportType = schedule.ReportType ?? ReportTypeConstants.AverageBasket;
            var parameters = DeserializeParameters(schedule.ParametersJson);
            var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

            byte[] csvBytes;
            if (string.Equals(reportType, ReportTypeConstants.PurchasesSales, StringComparison.OrdinalIgnoreCase))
            {
                var filter = new PurchasesSalesFilter
                {
                    DateFrom = dateFrom, DateTo = dateTo,
                    ReportMode = parameters.ReportMode,
                    PrimaryGroup = parameters.PrimaryGroup,
                    SecondaryGroup = parameters.SecondaryGroup,
                    ThirdGroup = parameters.ThirdGroup,
                    IncludeVat = parameters.IncludeVat,
                    ShowProfit = parameters.ShowProfit,
                    ShowStock = parameters.ShowStock,
                    StoreCodes = parameters.StoreCodes ?? new(),
                    ItemIds = parameters.ItemIds ?? new(),
                    SortColumn = parameters.SortColumn ?? "ItemCode",
                    SortDirection = parameters.SortDirection ?? "ASC",
                    PageSize = int.MaxValue
                };
                var repo = _repositoryFactory.CreatePurchasesSalesRepository(connString);
                var result = await repo.GetPurchasesSalesDataAsync(filter);
                csvBytes = new CsvExportService().GeneratePurchasesSalesCsv(result.Items, result.PsTotals, filter);
            }
            else
            {
                var filter = new ReportFilter
                {
                    DateFrom = dateFrom, DateTo = dateTo,
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
                csvBytes = new CsvExportService().GenerateAverageBasketCsv(result.Items, result.GrandTotals, filter);
            }

            if (csvBytes.Length > MaxCsvBytesForAi)
            {
                _logger.LogInformation(
                    "Schedule {Id}: CSV is {Size} bytes, truncating to {Max} for AI analysis",
                    schedule.ScheduleId, csvBytes.Length, MaxCsvBytesForAi);
                csvBytes = csvBytes[..MaxCsvBytesForAi];
            }

            var csvData = Encoding.UTF8.GetString(csvBytes);
            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, reportType, locale: schedule.AiLocale, ct: ct);

            _logger.LogInformation(
                "Schedule {Id}: AI analysis completed — {InTok}+{OutTok} tokens",
                schedule.ScheduleId, analysis.InputTokens, analysis.OutputTokens);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schedule {Id}: AI analysis failed — email will be sent without AI section", schedule.ScheduleId);
            return null;
        }
    }

    private static string BuildAiAnalysisHtml(ReportAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.Append(@"<div style='margin:24px 0;padding:20px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-family:Arial,sans-serif;'>");
        sb.Append(@"<h3 style='margin:0 0 12px;color:#6366f1;font-size:16px;'>");
        sb.Append(@"&#10024; AI Business Intelligence</h3>");

        if (!string.IsNullOrWhiteSpace(analysis.Summary))
        {
            sb.Append(@"<div style='padding:12px;background:#eef2ff;border-left:4px solid #6366f1;border-radius:4px;margin-bottom:16px;font-size:14px;color:#1e293b;'>");
            sb.Append(System.Net.WebUtility.HtmlEncode(analysis.Summary));
            sb.Append("</div>");
        }

        if (analysis.KeyFindings?.Count > 0)
        {
            sb.Append(@"<h4 style='margin:16px 0 8px;color:#1e293b;font-size:14px;'>&#128161; Key Findings</h4><ul style='margin:0;padding-left:20px;'>");
            foreach (var f in analysis.KeyFindings)
            {
                var text = f is string s ? s : f?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    sb.Append($"<li style='margin-bottom:4px;font-size:13px;color:#334155;'>{System.Net.WebUtility.HtmlEncode(text)}</li>");
            }
            sb.Append("</ul>");
        }

        if (analysis.Recommendations?.Count > 0)
        {
            sb.Append(@"<h4 style='margin:16px 0 8px;color:#1e293b;font-size:14px;'>&#9989; Recommendations</h4><ol style='margin:0;padding-left:20px;'>");
            foreach (var r in analysis.Recommendations)
            {
                var text = r is string s ? s : r?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    sb.Append($"<li style='margin-bottom:4px;font-size:13px;color:#334155;'>{System.Net.WebUtility.HtmlEncode(text)}</li>");
            }
            sb.Append("</ol>");
        }

        if (analysis.Alerts?.Count > 0)
        {
            sb.Append(@"<h4 style='margin:16px 0 8px;color:#dc2626;font-size:14px;'>&#9888;&#65039; Alerts</h4><ul style='margin:0;padding-left:20px;'>");
            foreach (var a in analysis.Alerts)
            {
                var text = a is string s ? s : a?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    sb.Append($"<li style='margin-bottom:4px;font-size:13px;color:#dc2626;'>{System.Net.WebUtility.HtmlEncode(text)}</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append($@"<p style='margin:16px 0 0;font-size:11px;color:#94a3b8;'>
Model: {System.Net.WebUtility.HtmlEncode(analysis.ModelUsed)} | 
Tokens: {analysis.InputTokens}+{analysis.OutputTokens} | 
Time: {(analysis.DurationMs / 1000.0):F1}s</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private async Task SendReportEmailAsync(
        ReportSchedule schedule, Database db,
        byte[] fileBytes, string fileName, string contentType,
        int rowCount, string period, string? storageKey, ReportAnalysis? aiAnalysis, CancellationToken ct)
    {
        var recipients = schedule.Recipients
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (recipients.Length == 0)
        {
            _logger.LogWarning("Schedule {Id} has no recipients — skipping email", schedule.ScheduleId);
            return;
        }

        string? downloadUrl = null;
        if (!string.IsNullOrEmpty(storageKey) && _storageService.IsConfigured)
        {
            try { downloadUrl = _storageService.GetDownloadUrl(storageKey, TimeSpan.FromDays(7)); }
            catch { /* pre-signed URL generation is best-effort */ }
        }

        var reportDisplayName = schedule.ReportType switch
        {
            ReportTypeConstants.PurchasesSales => "Purchases vs Sales",
            _ => "Average Basket"
        };
        var mergeValues = new Dictionary<string, string>
        {
            ["\u00ABReportName\u00BB"] = schedule.ScheduleName ?? reportDisplayName,
            ["\u00ABDatabaseName\u00BB"] = db.DBFriendlyName ?? "Unknown",
            ["\u00ABPeriod\u00BB"] = period,
            ["\u00ABRowCount\u00BB"] = rowCount.ToString(),
            ["\u00ABExportFormat\u00BB"] = schedule.ExportFormat ?? "Excel",
            ["\u00ABGeneratedDate\u00BB"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["\u00ABUserName\u00BB"] = schedule.CreatedBy ?? "Scheduler",
            ["\u00ABCompanyName\u00BB"] = db.DBFriendlyName ?? ""
        };

        // Try loading a template from the database
        EmailTemplate? template = null;
        try
        {
            var connString = !string.IsNullOrEmpty(_psCentralConnString)
                ? ConnectionStringBuilder.BuildFromReference(db, _psCentralConnString)
                : ConnectionStringBuilder.Build(db);
            var scheduleRepo = _repositoryFactory.CreateScheduleRepository(connString);
            template = await scheduleRepo.GetDefaultEmailTemplateAsync(schedule.ReportType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load email template for schedule {Id} — using default", schedule.ScheduleId);
        }

        string subject;
        string htmlBody;
        string textBody;

        string? aiHtml = aiAnalysis != null ? BuildAiAnalysisHtml(aiAnalysis) : null;

        if (template != null && !string.IsNullOrWhiteSpace(template.EmailBodyHtml))
        {
            subject = !string.IsNullOrWhiteSpace(schedule.EmailSubject)
                ? schedule.EmailSubject
                : ReplaceMergeFields(template.EmailSubject, mergeValues);
            htmlBody = ReplaceMergeFields(template.EmailBodyHtml, mergeValues);
            if (!string.IsNullOrEmpty(aiHtml))
                htmlBody += aiHtml;
            if (!string.IsNullOrEmpty(downloadUrl))
                htmlBody += BuildDownloadLinkHtml(downloadUrl);
            textBody = StripHtmlToPlainText(htmlBody);
        }
        else
        {
            subject = !string.IsNullOrWhiteSpace(schedule.EmailSubject)
                ? schedule.EmailSubject
                : $"Scheduled Report: {schedule.ScheduleName} — {db.DBFriendlyName}";
            htmlBody = BuildEmailHtml(schedule, db, rowCount, period, fileName, downloadUrl);
            if (!string.IsNullOrEmpty(aiHtml))
                htmlBody += aiHtml;
            textBody = BuildEmailText(schedule, db, rowCount, period, downloadUrl);
        }

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

    private static string ReplaceMergeFields(string text, Dictionary<string, string> values)
    {
        foreach (var (placeholder, value) in values)
            text = text.Replace(placeholder, value);
        text = text.Replace("\u00AB", "").Replace("\u00BB", "");
        return text;
    }

    private static string StripHtmlToPlainText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "</p>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "</tr>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    private static string BuildDownloadLinkHtml(string downloadUrl)
    {
        return $@"
    <p style='margin-top: 12px;'>
        <a href='{downloadUrl}' style='display:inline-block;padding:8px 16px;background:#2563eb;color:#fff;text-decoration:none;border-radius:4px;font-size:13px;'>
            Download Report
        </a>
        <br/><span style='color:#9ca3af;font-size:11px;'>Link valid for 7 days</span>
    </p>";
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

            // PS-specific fields
            if (root.TryGetProperty("reportMode", out var rm) || root.TryGetProperty("ReportMode", out rm))
                p.ReportMode = ParseEnum<PsReportMode>(rm);
            if (root.TryGetProperty("primaryGroup", out var pg) || root.TryGetProperty("PrimaryGroup", out pg))
                p.PrimaryGroup = ParseEnum<PsGroupBy>(pg);
            if (root.TryGetProperty("secondaryGroup", out var sg2p) || root.TryGetProperty("SecondaryGroup", out sg2p))
                p.SecondaryGroup = ParseEnum<PsGroupBy>(sg2p);
            if (root.TryGetProperty("thirdGroup", out var tg) || root.TryGetProperty("ThirdGroup", out tg))
                p.ThirdGroup = ParseEnum<PsGroupBy>(tg);
            if (root.TryGetProperty("showProfit", out var sp))
                p.ShowProfit = sp.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("showStock", out var ss))
                p.ShowStock = ss.ValueKind == JsonValueKind.True;

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
        int rowCount, string period, string fileName, string? downloadUrl = null)
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
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Rows</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{rowCount}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Format</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{schedule.ExportFormat}</td></tr>
    </table>
    <p style='color: #6b7280; font-size: 13px;'>
        File: {fileName}<br/>
        Generated: {DateTime.Now:yyyy-MM-dd HH:mm}
    </p>{(string.IsNullOrEmpty(downloadUrl) ? "" : $@"
    <p style='margin-top: 12px;'>
        <a href='{downloadUrl}' style='display:inline-block;padding:8px 16px;background:#2563eb;color:#fff;text-decoration:none;border-radius:4px;font-size:13px;'>
            Download Report
        </a>
        <br/><span style='color:#9ca3af;font-size:11px;'>Link valid for 7 days</span>
    </p>")}
    <p style='color: #9ca3af; font-size: 11px; margin-top: 24px;'>
        This is an automated report from Powersoft Reporting Engine.<br/>
        To modify or stop this schedule, log in to the Reporting dashboard.
    </p>
</div>";
    }

    private static string BuildEmailText(
        ReportSchedule schedule, Database db,
        int rowCount, string period, string? downloadUrl = null)
    {
        var text = $@"Scheduled Report: {schedule.ScheduleName}
Database: {db.DBFriendlyName}
Report Type: {schedule.ReportType}
Period: {period}
Rows: {rowCount}
Format: {schedule.ExportFormat}
Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";

        if (!string.IsNullOrEmpty(downloadUrl))
            text += $"\n\nDownload: {downloadUrl}\n(Link valid for 7 days)";

        text += "\n\nThis is an automated report from Powersoft Reporting Engine.";
        return text;
    }
}

public class ScheduleRunSummary
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}
