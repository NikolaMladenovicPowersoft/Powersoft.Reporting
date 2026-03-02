using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Services;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly ICentralRepository _centralRepository;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ReportsController> _logger;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ReportsController(
        ITenantRepositoryFactory repositoryFactory,
        ICentralRepository centralRepository,
        IEmailSender emailSender,
        ILogger<ReportsController> logger)
    {
        _repositoryFactory = repositoryFactory;
        _centralRepository = centralRepository;
        _emailSender = emailSender;
        _logger = logger;
    }
    
    private string? GetTenantConnectionString()
    {
        return HttpContext.Session.GetString(SessionKeys.TenantConnectionString);
    }
    
    private string? GetConnectedDatabaseName()
    {
        return HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);
    }

    private int GetRanking()
    {
        var fromSession = HttpContext.Session.GetInt32(SessionKeys.Ranking);
        if (fromSession.HasValue) return fromSession.Value;

        var claim = User.FindFirst(AppClaimTypes.Ranking)?.Value;
        if (int.TryParse(claim, out var ranking))
        {
            HttpContext.Session.SetInt32(SessionKeys.Ranking, ranking);
            return ranking;
        }
        return 99;
    }

    private int GetRoleID()
    {
        var fromSession = HttpContext.Session.GetInt32(SessionKeys.RoleID);
        if (fromSession.HasValue) return fromSession.Value;

        var claim = User.FindFirst(AppClaimTypes.RoleID)?.Value;
        if (int.TryParse(claim, out var roleId))
        {
            HttpContext.Session.SetInt32(SessionKeys.RoleID, roleId);
            return roleId;
        }
        return 0;
    }

    /// <summary>
    /// Reads MaxSchedulesPerReport from DB settings (tbl_Ini*), falling back to the compiled default.
    /// </summary>
    private async Task<int> GetMaxSchedulesPerReportAsync(string tenantConnString)
    {
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var ini = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderDbSettings,
                "ALL");

            var settings = DatabaseSettings.FromDictionary(ini);
            return settings.MaxSchedulesPerReport;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read DB settings, using default schedule limit");
            return ModuleConstants.ScheduleLimitDefault;
        }
    }

    /// <summary>
    /// Checks whether the current user is authorized for a specific action.
    /// Ranking &lt;= 20: all actions allowed.
    /// Ranking > 20: check tbl_RelRoleAction.
    /// </summary>
    private async Task<bool> IsActionAuthorizedAsync(int actionId)
    {
        var ranking = GetRanking();

        // System admin, client admin, client standard: all actions allowed
        if (ranking <= ModuleConstants.RankingAllActionsAllowed)
            return true;

        // Custom roles: check per-action permission
        var roleId = GetRoleID();
        return await _centralRepository.IsActionAuthorizedAsync(roleId, actionId);
    }

    public IActionResult Index()
    {
        var connectedDb = GetConnectedDatabaseName();
        if (string.IsNullOrEmpty(connectedDb))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }
        
        return View();
    }

    public async Task<IActionResult> AverageBasket(bool clearedFilters = false, bool layoutReset = false)
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();
        
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        // Action check: actionID 6025 = View Average Basket
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewAvgBasket))
        {
            _logger.LogWarning("User {User} denied access to Average Basket (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewAvgBasket);
            return RedirectToAction("AccessDenied", "Account");
        }
        
        var viewModel = new AverageBasketViewModel
        {
            ConnectedDatabase = connectedDb,
            IsConnected = true,
            DateFrom = new DateTime(DateTime.Today.Year, 1, 1),
            DateTo = DateTime.Today,
            CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleAvgBasket)
        };
        
        await ApplySavedLayoutAsync(viewModel, tenantConnString);
        await LoadAvailableStoresAsync(viewModel, tenantConnString);

        if (clearedFilters)
        {
            TempData["Success"] = viewModel.HasSavedLayout
                ? "Filters cleared. Displaying your saved layout."
                : "Filters cleared.";
        }
        else if (layoutReset)
        {
            TempData["Success"] = "Layout discarded. Reset to defaults.";
        }
        
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> AverageBasket(AverageBasketViewModel model)
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();
        
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewAvgBasket))
            return RedirectToAction("AccessDenied", "Account");
        
        model.ConnectedDatabase = connectedDb;
        model.IsConnected = true;
        model.CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleAvgBasket);
        
        await LoadAvailableStoresAsync(model, tenantConnString);
        ApplyDatePreset(model);
        
        var filter = model.ToReportFilter();
        if (!filter.IsValid(out var validationErrors))
        {
            model.ErrorMessage = string.Join(" ", validationErrors);
            return View(model);
        }
        
        try
        {
            var repo = _repositoryFactory.CreateAverageBasketRepository(tenantConnString);
            var result = await repo.GetAverageBasketDataAsync(filter);
            
            model.Results = result.Items;
            model.TotalCount = result.TotalCount;
            model.PageNumber = result.PageNumber;
            model.PageSize = result.PageSize;
            model.GrandTotals = result.GrandTotals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Average Basket report for period {DateFrom} to {DateTo}", 
                filter.DateFrom, filter.DateTo);
            model.ErrorMessage = "An error occurred while generating the report. Please try again.";
        }
        
        return View(model);
    }
    
    [HttpGet]
    public async Task<IActionResult> SearchItems(string? search, bool includeInactive = false)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { error = "Not connected to database" });

        try
        {
            var repo = _repositoryFactory.CreateItemRepository(tenantConnString);
            var items = await repo.SearchItemsAsync(search ?? "", includeInactive);
            return Json(items.Select(i => new { id = i.ItemId, code = i.ItemCode, name = i.ItemNamePrimary }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching items");
            return Json(new { error = "Failed to search items" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStores()
    {
        var tenantConnString = GetTenantConnectionString();
        
        if (string.IsNullOrEmpty(tenantConnString))
        {
            return Json(new { error = "Not connected to database" });
        }
        
        try
        {
            var repo = _repositoryFactory.CreateStoreRepository(tenantConnString);
            var stores = await repo.GetActiveStoresAsync();
            
            return Json(stores.Select(s => new 
            { 
                code = s.StoreCode, 
                name = s.StoreName,
                display = s.DisplayName 
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stores");
            return Json(new { error = "Failed to load stores" });
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> SaveSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson)
    {
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleAvgBasket))
            return Json(new { success = false, message = "You don't have permission to create schedules." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
            var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.AverageBasket);
            if (count >= maxSchedules)
                return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });

            var parsedTime = TimeSpan.TryParse(scheduleTime, out var ts) ? ts : new TimeSpan(8, 0, 0);
            DateTime? nextRun = null;

            if (string.Equals(recurrenceType, "Once", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(recurrenceJson))
                {
                    nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
                    if (nextRun == null)
                    {
                        var onceAt = RecurrenceNextRunCalculator.GetOnceScheduleDateTime(recurrenceJson);
                        if (onceAt.HasValue && onceAt.Value < DateTime.Now)
                            return Json(new { success = false, message = "For 'Run once', start date and time must be in the future." });
                        return Json(new { success = false, message = "For 'Run once', please set a valid start date and time in the future." });
                    }
                }
                else
                {
                    nextRun = CalculateNextRun("Once", recurrenceDay, parsedTime);
                }
            }
            else if (!string.IsNullOrWhiteSpace(recurrenceJson))
            {
                nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
            }

            if (nextRun == null)
                nextRun = CalculateNextRun(recurrenceType ?? "Daily", recurrenceDay, parsedTime);

            var schedule = new ReportSchedule
            {
                ReportType = ReportTypeConstants.AverageBasket,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = parametersJson,
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun
            };

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving report schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    // ==================== Email Templates ====================

    [HttpGet]
    public async Task<IActionResult> GetEmailTemplates()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var templates = await repo.GetEmailTemplatesAsync(ReportTypeConstants.AverageBasket);
            return Json(templates.Select(t => new
            {
                t.TemplateId, t.TemplateName, t.EmailSubject, t.EmailBodyHtml, t.IsDefault
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveEmailTemplate(string templateName, string emailSubject, string emailBodyHtml, bool isDefault = false)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (string.IsNullOrWhiteSpace(templateName))
            return Json(new { success = false, message = "Template name is required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var template = new ReportSchedule(); // Using EmailTemplate model
            var id = await repo.CreateEmailTemplateAsync(new Core.Models.EmailTemplate
            {
                TemplateName = templateName,
                ReportType = ReportTypeConstants.AverageBasket,
                EmailSubject = emailSubject ?? "",
                EmailBodyHtml = emailBodyHtml ?? "",
                IsDefault = isDefault,
                CreatedBy = User.Identity?.Name ?? "Unknown"
            });
            return Json(new { success = true, templateId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email template");
            return Json(new { success = false, message = "Failed to save template. The table may not exist yet." });
        }
    }

    // ==================== Layout Save/Restore ====================

    private string GetUserCode()
    {
        var fromSession = HttpContext.Session.GetString(SessionKeys.UserCode);
        if (!string.IsNullOrEmpty(fromSession)) return fromSession;

        var claim = User.FindFirst(AppClaimTypes.UserCode)?.Value;
        if (!string.IsNullOrEmpty(claim))
        {
            HttpContext.Session.SetString(SessionKeys.UserCode, claim);
            return claim;
        }
        return User.Identity?.Name ?? "UNKNOWN";
    }

    [HttpGet]
    public async Task<IActionResult> GetLayout()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                userCode);

            return Json(new { success = true, hasSaved = parms.Count > 0, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveLayout([FromBody] Dictionary<string, string> parameters)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (parameters == null || parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            await repo.SaveLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                ModuleConstants.IniDescriptionAvgBasket,
                userCode,
                parameters);

            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetLayout()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var deleted = await repo.DeleteLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                userCode);

            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    // ==================== Schedules ====================

    [HttpGet]
    public async Task<IActionResult> GetSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.AverageBasket);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                s.Recipients, scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    private DateTime? CalculateNextRun(string recurrenceType, int? day, TimeSpan time)
    {
        var now = DateTime.Now;
        var today = now.Date.Add(time);

        return recurrenceType switch
        {
            "Once" => today > now ? today : today.AddDays(1),
            "Daily" => today > now ? today : today.AddDays(1),
            "Weekly" => GetNextWeekday(now, day ?? 1, time),
            "Monthly" => GetNextMonthDay(now, day ?? 1, time),
            _ => today.AddDays(1)
        };
    }

    private DateTime GetNextWeekday(DateTime now, int dayOfWeek, TimeSpan time)
    {
        var target = (DayOfWeek)(dayOfWeek % 7);
        var daysUntil = ((int)target - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && now.TimeOfDay >= time) daysUntil = 7;
        return now.Date.AddDays(daysUntil).Add(time);
    }

    private DateTime GetNextMonthDay(DateTime now, int dayOfMonth, TimeSpan time)
    {
        var candidate = new DateTime(now.Year, now.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(now.Year, now.Month))).Add(time);
        if (candidate <= now) candidate = candidate.AddMonths(1);
        return candidate;
    }

    [HttpGet]
    public IActionResult ScheduleLogs()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        ViewBag.ConnectedDatabase = HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetScheduleLogs(int? scheduleId = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var logs = await repo.GetScheduleLogsAsync(scheduleId, top: 200);
            return Json(logs.Select(l => new
            {
                l.LogId, l.ScheduleId, l.ScheduleName, l.ReportType,
                runDate = l.RunDate.ToString("yyyy-MM-dd HH:mm:ss"),
                l.Status, l.RowsGenerated, l.FileSizeBytes,
                l.ErrorMessage, l.DurationMs
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> PrintPreview(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear, string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC")
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("AverageBasket");

        var model = new AverageBasketViewModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Breakdown = breakdown,
            GroupBy = groupBy,
            SecondaryGroupBy = secondaryGroupBy,
            IncludeVat = includeVat,
            CompareLastYear = compareLastYear,
            ConnectedDatabase = GetConnectedDatabaseName(),
            Results = result.Value.rows,
            GrandTotals = result.Value.totals,
            TotalCount = result.Value.rows.Count,
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear, string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC")
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("AverageBasket");

        var service = new ExcelExportService();
        var bytes = service.GenerateAverageBasketExcel(result.Value.rows, result.Value.totals, result.Value.filter);
        var filename = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpGet]
    public async Task<IActionResult> ExportPdf(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear, string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC")
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("AverageBasket");

        var service = new PdfExportService();
        var bytes = service.GenerateAverageBasketPdf(result.Value.rows, result.Value.totals, result.Value.filter);
        var filename = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear, string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC")
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("AverageBasket");

        var service = new CsvExportService();
        var bytes = service.GenerateAverageBasketCsv(result.Value.rows, result.Value.totals, result.Value.filter);
        var filename = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
        return File(bytes, "text/csv", filename);
    }

    // ==================== Send to Email ====================

    [HttpPost]
    public async Task<IActionResult> SendReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear,
        string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC")
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email format: {string.Join(", ", invalidEmails)}" });

        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy,
            includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                var pdfService = new PdfExportService();
                fileBytes = pdfService.GenerateAverageBasketPdf(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                var csvService = new CsvExportService();
                fileBytes = csvService.GenerateAverageBasketCsv(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                var excelService = new ExcelExportService();
                fileBytes = excelService.GenerateAverageBasketExcel(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"AverageBasket_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

        // Load template body if selected
        string? templateBody = null;
        string? templateSubject = null;
        if (templateId.HasValue && templateId > 0)
        {
            try
            {
                var tenantConnString = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConnString))
                {
                    var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
                    var tmpl = await schedRepo.GetEmailTemplateByIdAsync(templateId.Value);
                    if (tmpl != null)
                    {
                        templateBody = tmpl.EmailBodyHtml;
                        templateSubject = tmpl.EmailSubject;
                    }
                }
            }
            catch { /* fall back to default */ }
        }

        string ReplaceMergeFields(string text) => text
            .Replace("\u00ABReportName\u00BB", "Average Basket")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", result.Value.rows.Count.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Average Basket Report — {period}")
            : emailSubject;

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family: Arial, sans-serif; max-width: 600px;'>
    <h2 style='color: #2563eb;'>Average Basket Report</h2>
    <table style='border-collapse: collapse; width: 100%; margin: 16px 0;'>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Database</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Period</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Rows</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{result.Value.rows.Count}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Format</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <p style='color: #6b7280; font-size: 13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";

        var textBody = $"Average Basket Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var sentCount = 0;
        var errors = new List<string>();

        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send report email to {Email}", email);
                errors.Add(email);
            }
        }

        if (errors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", errors)}" });

        var msg = sentCount == 1
            ? $"Report sent to {emails[0]}"
            : $"Report sent to {sentCount} recipient(s)";
        if (errors.Count > 0)
            msg += $" (failed: {string.Join(", ", errors)})";

        return Json(new { success = true, message = msg });
    }

    private async Task<(List<AverageBasketRow> rows, ReportGrandTotals? totals, ReportFilter filter)?> RunExportQuery(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear, string? storeCodes, string? itemIds,
        string sortColumn, string sortDirection)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new ReportFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Breakdown = breakdown,
            GroupBy = groupBy,
            SecondaryGroupBy = secondaryGroupBy,
            IncludeVat = includeVat,
            CompareLastYear = compareLastYear,
            StoreCodes = string.IsNullOrEmpty(storeCodes) ? new() : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            ItemIds = string.IsNullOrEmpty(itemIds) ? new() : itemIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out _)).Select(s => int.Parse(s.Trim())).ToList(),
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            PageSize = int.MaxValue
        };

        try
        {
            var repo = _repositoryFactory.CreateAverageBasketRepository(tenantConnString);
            var result = await repo.GetAverageBasketDataAsync(filter);
            return (result.Items, result.GrandTotals, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting report");
            return null;
        }
    }

    private async Task ApplySavedLayoutAsync(AverageBasketViewModel model, string tenantConnString)
    {
        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                userCode);

            if (parms.Count == 0) return;

            model.HasSavedLayout = true;

            if (parms.TryGetValue("IncludeVat", out var iv))
                model.IncludeVat = iv == "1";
            if (parms.TryGetValue("CompareLastYear", out var cly))
                model.CompareLastYear = cly == "1";
            if (parms.TryGetValue("Breakdown", out var bd) && Enum.TryParse<BreakdownType>(bd, out var bdt))
                model.Breakdown = bdt;
            if (parms.TryGetValue("GroupBy", out var gb) && Enum.TryParse<GroupByType>(gb, out var gbt))
                model.GroupBy = gbt;
            if (parms.TryGetValue("SecondaryGroupBy", out var sgb) && Enum.TryParse<GroupByType>(sgb, out var sgbt))
                model.SecondaryGroupBy = sgbt;
            if (parms.TryGetValue("PageSize", out var ps) && int.TryParse(ps, out var pageSize) && pageSize > 0)
                model.PageSize = pageSize;
            if (parms.TryGetValue("HiddenColumns", out var hc) && !string.IsNullOrEmpty(hc))
                model.HiddenColumns = hc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load saved layout — using defaults");
        }
    }

    private async Task LoadAvailableStoresAsync(AverageBasketViewModel model, string tenantConnString)
    {
        try
        {
            var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
            model.AvailableStores = await storeRepo.GetActiveStoresAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stores");
            model.AvailableStores = new();
        }
    }
    
    private void ApplyDatePreset(AverageBasketViewModel model)
    {
        if (string.IsNullOrEmpty(model.DatePreset)) return;
        
        var today = DateTime.Today;
        
        switch (model.DatePreset)
        {
            case "today":
                model.DateFrom = today;
                model.DateTo = today;
                model.Breakdown = BreakdownType.Daily;
                break;
            case "yesterday":
                model.DateFrom = today.AddDays(-1);
                model.DateTo = today.AddDays(-1);
                model.Breakdown = BreakdownType.Daily;
                break;
            case "last7":
                model.DateFrom = today.AddDays(-6);
                model.DateTo = today;
                model.Breakdown = BreakdownType.Daily;
                break;
            case "last30":
                model.DateFrom = today.AddDays(-29);
                model.DateTo = today;
                model.Breakdown = BreakdownType.Daily;
                break;
            case "thisMonth":
                model.DateFrom = new DateTime(today.Year, today.Month, 1);
                model.DateTo = today;
                model.Breakdown = BreakdownType.Daily;
                break;
            case "lastMonth":
                var lastMonth = today.AddMonths(-1);
                model.DateFrom = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                model.DateTo = new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
                model.Breakdown = BreakdownType.Daily;
                break;
            case "ytd":
                model.DateFrom = new DateTime(today.Year, 1, 1);
                model.DateTo = today;
                model.Breakdown = BreakdownType.Monthly;
                break;
            case "lastYear":
                model.DateFrom = new DateTime(today.Year - 1, 1, 1);
                model.DateTo = new DateTime(today.Year - 1, 12, 31);
                model.Breakdown = BreakdownType.Monthly;
                break;
        }
    }
}
