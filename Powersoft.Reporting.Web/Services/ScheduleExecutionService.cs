using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Data.Tenant;
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
            databases = await _centralRepository.GetDatabasesLinkedToModuleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load module-linked databases from psCentral");
            summary.Errors.Add($"Failed to load databases: {ex.Message}");
            return summary;
        }

        _logger.LogInformation("Found {Count} module-linked databases to check", databases.Count);

        var semaphore = new SemaphoreSlim(4);
        var tasks = databases.Select(async db =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var connString = !string.IsNullOrEmpty(_psCentralConnString)
                    ? ConnectionStringBuilder.BuildFromReference(db, _psCentralConnString)
                    : ConnectionStringBuilder.Build(db);
                connString = ShortenTimeout(connString, 8);
                await ProcessDatabaseSchedulesAsync(db, connString, now, summary, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process schedules for database {DB}", db.DBFriendlyName);
                lock (summary) { summary.Errors.Add($"DB {db.DBFriendlyName}: {ex.Message}"); }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "Schedule runner completed. Processed: {Processed}, Succeeded: {Succeeded}, Failed: {Failed}",
            summary.Processed, summary.Succeeded, summary.Failed);

        return summary;
    }

    private static List<string> ParseJsonStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private static string ShortenTimeout(string connString, int seconds)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connString)
        {
            ConnectTimeout = seconds
        };
        return builder.ConnectionString;
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

        await SchemaMigrationService.EnsureSchemaAsync(connString);

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

            if (schedule.SkipIfEmpty && rowCount == 0)
            {
                _logger.LogInformation(
                    "Schedule {Id} skipped — 0 rows and SkipIfEmpty is enabled", schedule.ScheduleId);
                log.Status = ScheduleLogStatus.Skipped;
                log.ErrorMessage = "Skipped: report returned 0 rows (SkipIfEmpty enabled)";
                return;
            }

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

            summary.IncrementSucceeded();
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

            summary.IncrementFailed();
            _logger.LogError(ex,
                "Schedule {Id} '{Name}' failed after {Ms}ms",
                schedule.ScheduleId, schedule.ScheduleName, log.DurationMs);
        }
        finally
        {
            summary.IncrementProcessed();
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

        if (string.Equals(reportType, ReportTypeConstants.BelowMinStock, StringComparison.OrdinalIgnoreCase))
            return await GenerateBelowMinStockReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.Pareto, StringComparison.OrdinalIgnoreCase))
            return await GenerateParetoReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.Charts, StringComparison.OrdinalIgnoreCase))
            return await GenerateChartsReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.CancelLog, StringComparison.OrdinalIgnoreCase))
            return await GenerateCancelLogReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.Catalogue, StringComparison.OrdinalIgnoreCase))
            return await GenerateCatalogueReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.ProspectClients, StringComparison.OrdinalIgnoreCase))
            return await GenerateProspectClientsReportAsync(schedule, connString);

        if (string.Equals(reportType, ReportTypeConstants.OffersReport, StringComparison.OrdinalIgnoreCase))
            return await GenerateOffersReportAsync(schedule, connString);

        return await GenerateAverageBasketReportAsync(schedule, connString);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateCancelLogReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);
        (dateFrom, dateTo) = CancelLogScheduleDates.Normalize(dateFrom, dateTo, parameters.ReportByDateTime);

        _logger.LogInformation(
            "CancelLog schedule {Id}: dates {From:yyyy-MM-dd HH:mm} to {To:yyyy-MM-dd HH:mm}, byDateTime={ByDt}, action={Action}, clReport={ClRt}, hasItemsFilter={HasItems}",
            schedule.ScheduleId, dateFrom, dateTo, parameters.ReportByDateTime,
            parameters.CancelActionType ?? "All", parameters.CancelLogReportType ?? "Detailed",
            !string.IsNullOrWhiteSpace(parameters.ItemsSelectionJson));

        var filter = new CancelLogFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportByDateTime = parameters.ReportByDateTime,
            ActionType = Enum.TryParse<CancelLogActionType>(parameters.CancelActionType, true, out var at) ? at : CancelLogActionType.All,
            ReportType = Enum.TryParse<CancelLogReportType>(parameters.CancelLogReportType, true, out var rt) ? rt : CancelLogReportType.Detailed,
            PrimaryGroup = string.IsNullOrWhiteSpace(parameters.CancelLogPrimaryGroup) ? "NONE" : parameters.CancelLogPrimaryGroup,
            SecondaryGroup = string.IsNullOrWhiteSpace(parameters.CancelLogSecondaryGroup) ? "NONE" : parameters.CancelLogSecondaryGroup,
            TimezoneOffsetMinutes = parameters.TimezoneOffsetMinutes,
            MaxRecords = parameters.MaxRecords > 0 ? parameters.MaxRecords : 50000,
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
            SortColumn = string.IsNullOrWhiteSpace(parameters.SortColumn) ? "SessionDateTime" : parameters.SortColumn,
            SortDirection = parameters.SortDirection
        };

        var repo = _repositoryFactory.CreateCancelLogRepository(connString);

        List<CancelLogDetailedRow>? detailedRows = null;
        List<CancelLogSummaryRow>? summaryRows = null;
        int rowCount;
        if (filter.ReportType == CancelLogReportType.Summary)
        {
            var (rows, _) = await repo.GetSummaryAsync(filter);
            summaryRows = rows;
            rowCount = rows.Count;
        }
        else
        {
            var (rows, _) = await repo.GetDetailedAsync(filter);
            detailedRows = rows;
            rowCount = rows.Count;
        }

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateCancelLogPdf(detailedRows, summaryRows, filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateCancelLogCsv(detailedRows, summaryRows, filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateCancelLogExcel(detailedRows, summaryRows, filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (rowCount, fileBytes, fileName, contentType, period);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateCatalogueReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        // Mirrors ReportsController.BuildCatalogueFilterFromParams so a scheduled run produces
        // byte-for-byte the same filter the on-screen export would for the same parameters.
        var filter = new CatalogueFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            DateBasis = Enum.TryParse<CatalogueDateBasis>(parameters.CatDateBasis, true, out var cdb) ? cdb : CatalogueDateBasis.TransactionDate,
            UseDateTime = parameters.CatUseDateTime,
            ReportMode = Enum.TryParse<CatalogueReportMode>(parameters.CatReportMode, true, out var crm) ? crm : CatalogueReportMode.Detailed,
            ReportOn = Enum.TryParse<CatalogueReportOn>(parameters.CatReportOn, true, out var cro) ? cro : CatalogueReportOn.Sale,
            PrimaryGroup = Enum.TryParse<CatalogueGroupBy>(parameters.CatPrimaryGroup, true, out var cpg) ? cpg : CatalogueGroupBy.None,
            SecondaryGroup = Enum.TryParse<CatalogueGroupBy>(parameters.CatSecondaryGroup, true, out var csg) ? csg : CatalogueGroupBy.None,
            ThirdGroup = Enum.TryParse<CatalogueGroupBy>(parameters.CatThirdGroup, true, out var ctg) ? ctg : CatalogueGroupBy.None,
            ShowProfit = parameters.ShowProfit,
            ShowStock = parameters.ShowStock,
            StoreCodes = parameters.StoreCodes ?? new(),
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
            SortColumn = string.IsNullOrWhiteSpace(parameters.SortColumn) ? "ItemCode" : parameters.SortColumn,
            SortDirection = string.IsNullOrWhiteSpace(parameters.SortDirection) ? "ASC" : parameters.SortDirection,
            PageNumber = 1,
            PageSize = int.MaxValue,
            ProfitBasedOn = (CatalogueCostBasis)parameters.CatProfitBasedOn,
            ProfitIncludesVat = parameters.CatProfitIncludesVat,
            StockValueBasedOn = (CatalogueCostBasis)parameters.CatStockValueBasedOn,
            StockValueIncludesVat = parameters.CatStockValueIncludesVat,
            CostType = (CatalogueCostBasis)parameters.CatCostType
        };

        if (!string.IsNullOrWhiteSpace(parameters.CatDisplayColumns))
            filter.DisplayColumns = parameters.CatDisplayColumns
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Group cascade rules (same as the controller): an empty parent collapses its children.
        if (filter.PrimaryGroup == CatalogueGroupBy.None)
        {
            filter.SecondaryGroup = CatalogueGroupBy.None;
            filter.ThirdGroup = CatalogueGroupBy.None;
        }
        if (filter.SecondaryGroup == CatalogueGroupBy.None)
            filter.ThirdGroup = CatalogueGroupBy.None;

        // An explicit Include store selection in the Items widget wins over the legacy storeCodes list.
        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;

        ApplyCatalogueColumnFilters(filter, parameters.CatColumnFilters);

        var repo = _repositoryFactory.CreateCatalogueRepository(connString);
        var result = await repo.GetCatalogueDataAsync(filter);
        var rows = result.Items;
        CatalogueTotals? totals = filter.ReportOn != CatalogueReportOn.Both
            ? await repo.GetCatalogueTotalsAsync(filter)
            : null;

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        // Permission flags baked into ParametersJson at schedule creation time
        bool viewCost     = parameters.ViewCost;
        bool viewSupplier = parameters.ViewSupplier;

        switch (format)
        {
            case "csv":
                fileBytes = new CsvExportService().GenerateCatalogueCsv(rows, totals, filter, viewCost, viewSupplier);
                fileName = $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                // Catalogue has no dedicated PDF export — fall back to Excel for any non-CSV format.
                fileBytes = new ExcelExportService().GenerateCatalogueExcel(rows, totals, filter, viewCost, viewSupplier);
                fileName = $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (rows.Count, fileBytes, fileName, contentType, period);
    }

    /// <summary>
    /// Replicates ReportsController.ApplyColumnFiltersFromJson for the headless scheduler path so
    /// per-column grid filters saved with a Catalogue schedule are honoured at execution time.
    /// </summary>
    private static void ApplyCatalogueColumnFilters(CatalogueFilter filter, string? columnFilters)
    {
        if (string.IsNullOrWhiteSpace(columnFilters)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(columnFilters);
            if (doc.RootElement.TryGetProperty("values", out var vals) && vals.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in vals.EnumerateObject())
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(v)) filter.FilterValues[prop.Name] = v;
                }
            }
            if (doc.RootElement.TryGetProperty("operators", out var ops) && ops.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in ops.EnumerateObject())
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(v)) filter.FilterOperators[prop.Name] = v;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON — ignore, run without column filters (same as the controller).
        }
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateChartsReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new ChartFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Mode = Enum.TryParse<ChartMode>(parameters.ChartMode, true, out var cmode) ? cmode : ChartMode.Sales,
            Dimension = Enum.TryParse<ChartDimension>(parameters.ChartDimension, true, out var cdim) ? cdim : ChartDimension.Category,
            Metric = Enum.TryParse<ChartMetric>(parameters.ChartMetric, true, out var cmet) ? cmet : ChartMetric.Value,
            TopN = parameters.TopN > 0 ? parameters.TopN : 10,
            ShowOthers = parameters.ShowOthers,
            CompareLastYear = parameters.CompareLastYear,
            IncludeVat = parameters.IncludeVat,
            StoreCodes = parameters.StoreCodes,
            ChartType = string.IsNullOrWhiteSpace(parameters.ChartType) ? "pie" : parameters.ChartType,
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson)
        };

        var repo = _repositoryFactory.CreateChartRepository(connString);
        var data = await repo.GetSalesBreakdownAsync(filter);

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateChartPdf(data, filter);
                fileName = $"Chart_{filter.Dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateChartCsv(data, filter);
                fileName = $"Chart_{filter.Dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateChartExcel(data, filter);
                fileName = $"Chart_{filter.Dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (data.Count, fileBytes, fileName, contentType, period);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateParetoReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new ParetoFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Dimension = Enum.TryParse<ParetoDimension>(parameters.ParetoDimension, true, out var pdim) ? pdim : ParetoDimension.Item,
            Metric = Enum.TryParse<ParetoMetric>(parameters.ParetoMetric, true, out var pmet) ? pmet : ParetoMetric.Value,
            IncludeVat = parameters.IncludeVat,
            StoreCodes = parameters.StoreCodes,
            ClassAThreshold = parameters.ClassAThreshold,
            ClassBThreshold = parameters.ClassBThreshold,
            ExcludeNegativeAmounts = parameters.ExcludeNegativeAmounts,
            ProfitBasis = Enum.IsDefined(typeof(ParetoProfitBasis), parameters.ProfitBasis)
                ? (ParetoProfitBasis)parameters.ProfitBasis : ParetoProfitBasis.LatestCost,
            TimezoneOffsetMinutes = parameters.TimezoneOffsetMinutes,
            PriceInterval = parameters.PriceInterval,
            PriceOnIndex = parameters.PriceOnIndex,
            PriceOnIncludesVat = parameters.PriceOnIncludesVat,
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson)
        };

        var repo = _repositoryFactory.CreateParetoRepository(connString);
        var result = await repo.GetParetoDataAsync(filter);

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateParetoPdf(result, filter, parameters.ViewCost);
                fileName = $"Pareto_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateParetoCsv(result, filter, parameters.ViewCost);
                fileName = $"Pareto_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateParetoExcel(result, filter, parameters.ViewCost);
                fileName = $"Pareto_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        return (result.Rows.Count, fileBytes, fileName, contentType, period);
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
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
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
            ShowOnOrder = parameters.ShowOnOrder,
            ShowReservation = parameters.ShowReservation,
            ShowAvailable = parameters.ShowAvailable,
            IncludeAdditionalCharges = parameters.IncludeAdditionalCharges,
            StoreCodes = parameters.StoreCodes ?? new(),
            ItemIds = parameters.ItemIds ?? new(),
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
            SortColumn = parameters.SortColumn ?? "ItemCode",
            SortDirection = parameters.SortDirection ?? "ASC",
            PageSize = int.MaxValue
        };
        if (filter.ItemsSelection?.Stores is { HasFilter: true, Mode: FilterMode.Include })
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;

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

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateBelowMinStockReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = DeserializeParameters(schedule.ParametersJson);

        var filter = new BelowMinStockFilter
        {
            StoreCodes = parameters.StoreCodes,
            ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
            SortColumn = parameters.SortColumn ?? "ItemCode",
            SortDirection = parameters.SortDirection ?? "ASC"
        };

        var repo = _repositoryFactory.CreateBelowMinStockRepository(connString);
        var data = await repo.GetBelowMinStockAsync(filter);

        var format = schedule.ExportFormat?.ToLowerInvariant() ?? "excel";
        byte[] fileBytes;
        string fileName;
        string contentType;

        bool viewCost = parameters.ViewCost;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ItemCode,ItemName,Store,StoreName,Category,Department,Brand,CurrentStock,MinimumStock,Difference," + (viewCost ? "Cost,StockValue," : "") + "Shelf");
        foreach (var r in data)
        {
            sb.AppendLine($"\"{r.ItemCode}\",\"{r.ItemName}\",\"{r.StoreCode}\",\"{r.StoreName}\"," +
                $"\"{r.CategoryName}\",\"{r.DepartmentName}\",\"{r.BrandName}\"," +
                $"{r.CurrentStock},{r.MinimumStock},{r.Difference}," + (viewCost ? $"{r.Cost ?? 0},{r.StockValue ?? 0}," : "") + $"\"{r.Shelf}\"");
        }

        switch (format)
        {
            case "csv":
                fileBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                fileName = $"BelowMinStock_{DateTime.Now:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                fileName = $"BelowMinStock_{DateTime.Now:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
        }

        return (data.Count, fileBytes, fileName, contentType, $"As of {DateTime.Now:yyyy-MM-dd}");
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateProspectClientsReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = ScheduleParametersParser.Parse(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new ProspectClientsFilter
        {
            DateFrom       = dateFrom,
            DateTo         = dateTo,
            DateField      = parameters.PcDateField     ?? "RegistrationDate",
            StatusFilter   = parameters.PcStatusFilter  ?? "All",
            PriorityFilter = parameters.PcPriorityFilter  ?? "All",
            FollowedByFilter   = parameters.PcFollowedByFilter ?? "All",
            Category1Filter    = parameters.PcCategory1Filter  ?? "All",
            Category2Filter    = parameters.PcCategory2Filter  ?? "All",
            PrimaryGroup       = parameters.PcPrimaryGroup   ?? "NONE",
            SecondaryGroup     = parameters.PcSecondaryGroup ?? "NONE",
            MaxRecords         = parameters.MaxRecords > 0 ? parameters.MaxRecords : 50000,
            SortColumn         = parameters.SortColumn ?? "RegistrationDate",
            SortDirection      = parameters.SortDirection ?? "DESC",
            IncludeHistory     = parameters.PcIncludeHistory,
            CustomerCodes      = ParseJsonStringList(parameters.PcCustomerCodesJson),
            CustomerExcludeMode = parameters.PcCustomerExcludeMode
        };

        var repo = _repositoryFactory.CreateProspectClientsRepository(connString);
        var (rows, _) = await repo.GetDataAsync(filter);
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

        byte[] fileBytes;
        string fileName, contentType;
        switch (schedule.ExportFormat?.ToUpperInvariant())
        {
            case "CSV":
                fileBytes   = new CsvExportService().GenerateProspectClientsCsv(rows, filter);
                fileName    = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            case "PDF":
                fileBytes   = new PdfExportService().GenerateProspectClientsPdf(rows, filter);
                fileName    = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            default:
                fileBytes   = new ExcelExportService().GenerateProspectClientsExcel(rows, filter);
                fileName    = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        return (rows.Count, fileBytes, fileName, contentType, period);
    }

    private async Task<(int rowCount, byte[] fileBytes, string fileName, string contentType, string period)>
        GenerateOffersReportAsync(ReportSchedule schedule, string connString)
    {
        var parameters = ScheduleParametersParser.Parse(schedule.ParametersJson);
        var (dateFrom, dateTo) = DateRangeResolver.Resolve(parameters.DateRange);

        var filter = new OffersReportFilter
        {
            DateFrom       = dateFrom,
            DateTo         = dateTo,
            DateField      = parameters.OrDateField    ?? "DateTrans",
            StatusFilter   = parameters.OrStatusFilter ?? "All",
            StoreFilter    = parameters.OrStoreFilter  ?? "All",
            AgentFilter    = parameters.OrAgentFilter  ?? "All",
            PrimaryGroup   = parameters.OrPrimaryGroup   ?? "NONE",
            SecondaryGroup = parameters.OrSecondaryGroup ?? "NONE",
            ThirdGroup     = parameters.OrThirdGroup     ?? "NONE",
            MaxRecords     = parameters.MaxRecords > 0 ? parameters.MaxRecords : 50000,
            SortColumn     = parameters.SortColumn    ?? "DateTrans",
            SortDirection  = parameters.SortDirection ?? "DESC",
            OfferType           = parameters.OrOfferType   ?? "All",
            IncludeHistory      = parameters.OrIncludeHistory,
            CustomerCodes       = ParseJsonStringList(parameters.OrCustomerCodesJson),
            CustomerExcludeMode = parameters.OrCustomerExcludeMode,
            StatusCodes    = ParseJsonStringList(parameters.OrStatusCodesJson),
            StoreCodes     = ParseJsonStringList(parameters.OrStoreCodesJson),
            AgentCodes     = ParseJsonStringList(parameters.OrAgentCodesJson),
            ItemsSelectionJson = string.IsNullOrWhiteSpace(parameters.ItemsSelectionJson) ? null : parameters.ItemsSelectionJson
        };

        var repo = _repositoryFactory.CreateOffersReportRepository(connString);
        var (rows, _) = await repo.GetDataAsync(filter);
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

        byte[] fileBytes;
        string fileName, contentType;
        switch (schedule.ExportFormat?.ToUpperInvariant())
        {
            case "CSV":
                fileBytes   = new CsvExportService().GenerateOffersReportCsv(rows, filter, parameters.ViewCost);
                fileName    = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            case "PDF":
                fileBytes   = new PdfExportService().GenerateOffersReportPdf(rows, filter, parameters.ViewCost);
                fileName    = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            default:
                fileBytes   = new ExcelExportService().GenerateOffersReportExcel(rows, filter, parameters.ViewCost);
                fileName    = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        return (rows.Count, fileBytes, fileName, contentType, period);
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
                    ShowOnOrder = parameters.ShowOnOrder,
                    ShowReservation = parameters.ShowReservation,
                    ShowAvailable = parameters.ShowAvailable,
                    IncludeAdditionalCharges = parameters.IncludeAdditionalCharges,
                    StoreCodes = parameters.StoreCodes ?? new(),
                    ItemIds = parameters.ItemIds ?? new(),
                    ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
                    SortColumn = parameters.SortColumn ?? "ItemCode",
                    SortDirection = parameters.SortDirection ?? "ASC",
                    PageSize = int.MaxValue
                };
                if (filter.ItemsSelection?.Stores is { HasFilter: true, Mode: FilterMode.Include })
                    filter.StoreCodes = filter.ItemsSelection.Stores.Ids;
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
                    ItemsSelection = ItemsSelectionParser.Parse(parameters.ItemsSelectionJson),
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

    // Delegates to Core so the parsing is unit-testable without the web host.
    // See Powersoft.Reporting.Core.Helpers.ScheduleParametersParser.
    private static ScheduleParameters DeserializeParameters(string? json)
        => ScheduleParametersParser.Parse(json);

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
        var downloadBlock = string.IsNullOrEmpty(downloadUrl) ? "" : $@"
    <p style='margin-top:16px;'>
      <a href='{downloadUrl}' style='display:inline-block;padding:10px 20px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;font-size:13px;font-weight:600;'>
        Download Report
      </a>
      <br/><span style='color:#9ca3af;font-size:11px;margin-top:4px;display:inline-block;'>Link valid for 7 days</span>
    </p>";

        return $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto;'>
  <table width='100%' cellpadding='0' cellspacing='0' border='0'><tr>
    <td style='background-color:#1e40af;padding:24px 32px;'>
      <h1 style='margin:0;color:#ffffff;font-size:20px;font-weight:600;'>Powersoft Reports</h1>
    </td>
  </tr></table>
  <div style='background-color:#ffffff;padding:28px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;'>
    <h2 style='margin:0 0 8px;color:#1e40af;font-size:18px;font-weight:700;'>{schedule.ScheduleName}</h2>
    <p style='margin:0 0 20px;color:#374151;font-size:14px;'>{db.DBFriendlyName}</p>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 20px;font-size:14px;'>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;width:120px;'>Report Type</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;font-weight:600;'>{schedule.ReportType}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Period</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{period}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Rows</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{rowCount:N0}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{schedule.ExportFormat}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;color:#6b7280;'>Generated</td>
        <td style='padding:10px 14px;color:#111827;'>{DateTime.Now:yyyy-MM-dd HH:mm}</td>
      </tr>
    </table>{downloadBlock}
  </div>
  <table width='100%' cellpadding='0' cellspacing='0' border='0'><tr>
    <td style='background-color:#f3f4f6;padding:16px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;border-bottom:1px solid #d1d5db;'>
      <p style='margin:0;color:#6b7280;font-size:11px;'>
        Automated report by Powersoft Report Engine &bull; {db.DBFriendlyName}<br/>
        To modify or stop this schedule, log in to the Reporting dashboard.
      </p>
    </td>
  </tr></table>
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
    private int _processed;
    private int _succeeded;
    private int _failed;

    public int Processed => _processed;
    public int Succeeded => _succeeded;
    public int Failed => _failed;

    public void IncrementProcessed() => Interlocked.Increment(ref _processed);
    public void IncrementSucceeded() => Interlocked.Increment(ref _succeeded);
    public void IncrementFailed() => Interlocked.Increment(ref _failed);

    public List<string> Errors { get; } = new();
}
