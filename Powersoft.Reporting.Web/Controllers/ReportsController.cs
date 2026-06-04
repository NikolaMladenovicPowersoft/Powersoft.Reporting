using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;
using System.Text.Json;
using Powersoft.Reporting.Web.Services;
using Powersoft.Reporting.Web.Services.AI;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly ICentralRepository _centralRepository;
    private readonly IEmailSender _emailSender;
    private readonly ReportAnalyzerFactory _analyzerFactory;
    private readonly IFilterPresetRepository _filterPresetRepo;
    private readonly ILogger<ReportsController> _logger;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ReportsController(
        ITenantRepositoryFactory repositoryFactory,
        ICentralRepository centralRepository,
        IEmailSender emailSender,
        ReportAnalyzerFactory analyzerFactory,
        IFilterPresetRepository filterPresetRepo,
        ILogger<ReportsController> logger)
    {
        _repositoryFactory = repositoryFactory;
        _centralRepository = centralRepository;
        _emailSender = emailSender;
        _analyzerFactory = analyzerFactory;
        _filterPresetRepo = filterPresetRepo;
        _logger = logger;
    }
    
    private static List<string> ParseCustomerCodesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    /// <summary>
    /// Injects server-side permission flags (ViewCost, ViewSupplier) into the schedule ParametersJson.
    /// The background scheduler runs without a user session — permissions must be baked into the saved JSON.
    /// </summary>
    private string InjectPermissionsIntoParametersJson(string? json)
    {
        System.Text.Json.Nodes.JsonObject dict;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { dict = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? new(); }
            catch { dict = new(); }
        }
        else { dict = new(); }
        dict["ViewCost"]     = CanViewCost();
        dict["ViewSupplier"] = CanViewSupplier();
        return dict.ToJsonString();
    }

    private static (string[] valid, string[] invalid) ParseAndValidateEmailList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (Array.Empty<string>(), Array.Empty<string>());

        var all = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = all.Where(e => EmailRegex.IsMatch(e)).ToArray();
        var invalid = all.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        return (valid, invalid);
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

    private ItemsSelectionFilter? ParseItemsSelection(string? json)
    {
        var result = ItemsSelectionParser.Parse(json);
        if (result == null && !string.IsNullOrWhiteSpace(json))
            _logger.LogWarning("ParseItemsSelection failed — selections ignored");
        return result;
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

    /// <summary>
    /// Returns true if the current user may see cost/profit/margin data.
    /// Legacy action 6015 (ViewCost). Resolved at login and stored as a claim.
    /// Ranking &lt;= 20: always true. Ranking > 20: depends on tbl_RelRoleAction.
    /// </summary>
    private bool CanViewCost()
    {
        if (GetRanking() <= ModuleConstants.RankingAllActionsAllowed) return true;
        var claim = User.FindFirst(AppClaimTypes.ViewCost);
        return claim == null || !bool.TryParse(claim.Value, out var v) || v;
    }

    /// <summary>
    /// Returns true if the current user may see supplier information.
    /// Legacy action 1200 (View Suppliers). Resolved at login and stored as a claim.
    /// </summary>
    private bool CanViewSupplier()
    {
        if (GetRanking() <= ModuleConstants.RankingAllActionsAllowed) return true;
        var claim = User.FindFirst(AppClaimTypes.ViewSupplier);
        return claim == null || !bool.TryParse(claim.Value, out var v) || v;
    }

    /// <summary>
    /// Removes cost/profit/margin and supplier column tokens from a Catalogue
    /// DisplayColumns CSV string when the current user lacks the matching right.
    /// Used for print preview, which renders columns purely from the display-column
    /// selection (no JS-based hiding applies to printable output).
    /// </summary>
    private string StripRestrictedCatalogueColumns(string? displayColumns)
    {
        if (string.IsNullOrWhiteSpace(displayColumns)) return displayColumns ?? string.Empty;

        var tokens = displayColumns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!CanViewCost())
        {
            blocked.Add("Profit");
            blocked.Add("Markup");
            blocked.Add("Margin");
            blocked.Add("Cost");
            blocked.Add("TotalCost");
        }
        if (!CanViewSupplier())
            blocked.Add("ItemSupplier");

        if (blocked.Count == 0) return displayColumns;
        return string.Join(",", tokens.Where(t => !blocked.Contains(t)));
    }

    public async Task<IActionResult> Index()
    {
        var connectedDb = GetConnectedDatabaseName();
        if (string.IsNullOrEmpty(connectedDb))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        // For Ranking > 20 (custom roles) — resolve per-report view permissions so
        // the dashboard and sidebar can hide inaccessible reports.
        // Ranking <= 20: all true (shortcut — no DB calls).
        var ranking = GetRanking();
        if (ranking <= ModuleConstants.RankingAllActionsAllowed)
        {
            ViewBag.CanViewAB         = true;
            ViewBag.CanViewPS         = true;
            ViewBag.CanViewPareto     = true;
            ViewBag.CanViewCharts     = true;
            ViewBag.CanViewCatalogue  = true;
            ViewBag.CanViewBMS        = true;
            ViewBag.CanViewCancelLog  = true;
            ViewBag.CanViewPC         = true;
            ViewBag.CanViewOffers     = true;
        }
        else
        {
            var roleId = GetRoleID();
            // Run all 9 checks concurrently — single await at end
            var t1  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewAvgBasket);
            var t2  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewPurchasesSales);
            var t3  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewPareto);
            var t4  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCharts);
            var t5  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCatalogue);
            var t6  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewBelowMinStock);
            var t7  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCancelLog);
            var t8  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewProspectClients);
            var t9  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewOffersReport);
            await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8, t9);

            ViewBag.CanViewAB         = t1.Result;
            ViewBag.CanViewPS         = t2.Result && CanViewCost();
            ViewBag.CanViewPareto     = t3.Result;
            ViewBag.CanViewCharts     = t4.Result;
            ViewBag.CanViewCatalogue  = t5.Result;
            ViewBag.CanViewBMS        = t6.Result;
            ViewBag.CanViewCancelLog  = t7.Result;
            ViewBag.CanViewPC         = t8.Result;
            ViewBag.CanViewOffers     = t9.Result;
        }

        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();
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

        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();
        
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
        
        if (model.IsPeopleCount)
        {
            model.Breakdown = BreakdownType.Daily;
            model.GroupBy = GroupByType.None;
            model.SecondaryGroupBy = GroupByType.Store;
            model.CompareLastYear = false;
            model.PageSize = 1000;
        }
        
        var filter = model.ToReportFilter();
        filter.ItemsSelection = ParseItemsSelection(model.ItemsSelectionJson);
        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
        {
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;
        }
        if (!filter.IsValid(out var validationErrors))
        {
            model.ErrorMessage = string.Join(" ", validationErrors);
            return View(model);
        }
        
        try
        {
            var repo = _repositoryFactory.CreateAverageBasketRepository(tenantConnString);
            var result = await repo.GetAverageBasketDataAsync(filter);
            
            var items = result.Items;
            if (model.IsPeopleCount)
                items = items.Where(r => r.CYAllTransactions > 0).ToList();

            model.Results = items;
            model.TotalCount = model.IsPeopleCount ? items.Count : result.TotalCount;
            model.PageNumber = result.PageNumber;
            model.PageSize = model.IsPeopleCount ? items.Count : result.PageSize;
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

    [HttpGet]
    public async Task<IActionResult> GetDimensions(string type, string? search = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { error = "Not connected to database" });

        try
        {
            var typeNorm = type?.ToLowerInvariant();
            // Items modal uses this endpoint — must return rows from tbl_Item (was empty → Include Items never worked).
            if (typeNorm is "item" or "items")
            {
                var itemRepo = _repositoryFactory.CreateItemRepository(tenantConnString);
                var items = await itemRepo.SearchItemsAsync(search, includeInactive: false, maxResults: 300);
                var dimItems = items.Select(i => new DimensionItem
                {
                    Id = i.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Code = i.ItemCode,
                    Name = string.IsNullOrWhiteSpace(i.ItemNamePrimary) ? i.ItemCode : i.ItemNamePrimary!
                }).ToList();
                return Json(dimItems);
            }

            var repo = _repositoryFactory.CreateDimensionRepository(tenantConnString);
            List<DimensionItem> results = typeNorm switch
            {
                "category" or "categories" => await repo.GetCategoriesAsync(),
                "department" or "departments" => await repo.GetDepartmentsAsync(),
                "brand" or "brands" => await repo.GetBrandsAsync(),
                "season" or "seasons" => await repo.GetSeasonsAsync(),
                "supplier" or "suppliers" => await repo.GetSuppliersAsync(search),
                "customer" or "customers" => await repo.GetCustomersAsync(search),
                "agent" or "agents" => await repo.GetAgentsAsync(search),
                "postalcode" or "postalcodes" => await repo.GetPostalCodesAsync(search),
                "paymenttype" or "paymenttypes" => await repo.GetPaymentTypesAsync(),
                "zreport" or "zreports" => await repo.GetZReportsAsync(search),
                "town" or "towns" => await repo.GetTownsAsync(search),
                "user" or "users" => await repo.GetUsersAsync(search),
                "store" or "stores" => new List<DimensionItem>(),
                "model" or "models" => await repo.GetModelsAsync(),
                "colour" or "colours" => await repo.GetColoursAsync(),
                "size" or "sizes" => await repo.GetSizesAsync(),
                "groupsize" or "groupsizes" => await repo.GetGroupSizesAsync(),
                "fabric" or "fabrics" => await repo.GetFabricsAsync(),
                "attr1" => await repo.GetAttributeValuesAsync(1),
                "attr2" => await repo.GetAttributeValuesAsync(2),
                "attr3" => await repo.GetAttributeValuesAsync(3),
                "attr4" => await repo.GetAttributeValuesAsync(4),
                "attr5" => await repo.GetAttributeValuesAsync(5),
                "attr6" => await repo.GetAttributeValuesAsync(6),
                _ => new List<DimensionItem>()
            };
            return Json(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dimensions: {Type}", type);
            return Json(new { error = $"Failed to load {type}" });
        }
    }
    
    [HttpGet]
    public async Task<IActionResult> GetEntityDetail(string code, string type)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        try
        {
            var isCustomer = type?.ToLowerInvariant() == "customer";
            var table = isCustomer ? "tbl_Customer" : "tbl_Supplier";
            var pk = isCustomer ? "pk_CustomerNo" : "pk_SupplierNo";

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT TOP 1
                {pk} AS Code,
                CASE WHEN ISNULL(Company,0) = 1 THEN LastCompanyName ELSE FirstName + ' ' + LastCompanyName END AS FullName,
                ISNULL(ShortName,'') AS ShortName,
                ISNULL({(isCustomer ? "CustomerId" : "SupplierId")},'') AS EntityId,
                ISNULL(Address1,'') AS Address1, ISNULL(Address2,'') AS Address2,
                ISNULL(Town,'') AS Town, ISNULL(PostalCode,'') AS PostalCode,
                ISNULL(Tel1,'') AS Phone, ISNULL(Mobile,'') AS Mobile,
                ISNULL(Email,'') AS Email, ISNULL(VAT_Registration_No,'') AS VatNo,
                CASE WHEN ISNULL(Active,0) = 1 THEN 'Active' ELSE 'Inactive' END AS Status
                FROM {table} WHERE {pk} = @Code";
            cmd.Parameters.AddWithValue("@Code", code ?? "");
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Json(new { success = false, message = $"{(isCustomer ? "Customer" : "Supplier")} not found" });

            var result = new Dictionary<string, string>();
            for (int i = 0; i < reader.FieldCount; i++)
                result[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";

            return Json(new { success = true, data = result, entityType = isCustomer ? "Customer" : "Supplier" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading entity detail: {Code}", code);
            return Json(new { success = false, message = "Failed to load entity details" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
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

            // Quota only applies to new schedules — editing an existing one doesn't consume a new slot.
            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.AverageBasket);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

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
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.AverageBasket);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving report schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    // ==================== Email Templates ====================

    [HttpGet]
    public async Task<IActionResult> GetEmailTemplates(string? reportType = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var templates = await repo.GetEmailTemplatesAsync(reportType);
            return Json(templates.Select(t => new
            {
                t.TemplateId, t.TemplateName, t.EmailSubject, t.EmailBodyHtml, t.IsDefault, t.ReportType
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveEmailTemplate(string templateName, string emailSubject, string emailBodyHtml, string? reportType = null, bool isDefault = false)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (string.IsNullOrWhiteSpace(templateName))
            return Json(new { success = false, message = "Template name is required" });

        if (string.IsNullOrWhiteSpace(reportType))
            reportType = null;

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var id = await repo.CreateEmailTemplateAsync(new Core.Models.EmailTemplate
            {
                TemplateName = templateName,
                ReportType = reportType,
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

    // ==================== Average Basket Named Layouts ====================

    [HttpGet]
    public async Task<IActionResult> ListAbLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                userCode);

            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode,
                    name = l.Name,
                    isPublic = l.IsPublic,
                    createdBy = l.CreatedBy,
                    canEdit = l.CanEdit,
                    lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing AB layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveAbLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveAbLayoutAs([FromBody] SaveAbLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderAvgBasket,
                ModuleConstants.IniDescriptionAvgBasket,
                userCode,
                req.Name,
                req.IsPublic,
                req.Parameters);

            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving named AB layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadAbLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });

            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading AB layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAbLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AB layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    // ==================== Schedules ====================

    // Schedules are scoped to the user who created them. Only the creator can
    // load / edit / delete an existing schedule. The create path is still gated by
    // the per-report action permission (ActionSchedule* in ModuleConstants).
    // Admin override ("ranking below system-admin can edit any schedule") is
    // intentionally NOT wired up here yet — needs Christina's go-ahead + BMS action
    // rows in dboActionsDef. Until then, no cross-user mutation is allowed.
    private string CurrentScheduleOwnerId() => User.Identity?.Name ?? "Unknown";

    private (bool ok, string message) ValidateScheduleForMutation(
        ReportSchedule? existing, string? expectedReportType)
    {
        if (existing == null)
            return (false, "Schedule not found.");
        if (!existing.IsActive)
            return (false, "This schedule has been deleted. Reload the list and try again.");
        if (expectedReportType != null &&
            !string.Equals(existing.ReportType, expectedReportType, StringComparison.OrdinalIgnoreCase))
            return (false, "Schedule belongs to another report.");
        if (!string.Equals(existing.CreatedBy, CurrentScheduleOwnerId(), StringComparison.OrdinalIgnoreCase))
            return (false, "You can only modify schedules you created.");
        return (true, string.Empty);
    }

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

    // Generic load endpoint — returns full schedule record (including ParametersJson, RecurrenceJson,
    // Recipients, Subject, AI flags) so the UI can rehydrate the modal when user clicks an existing row.
    // Works for ANY report type — client decides how to interpret ParametersJson.
    [HttpGet]
    public async Task<IActionResult> GetScheduleById(int scheduleId)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (scheduleId <= 0)
            return Json(new { success = false, message = "Invalid schedule id" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var s = await repo.GetScheduleByIdAsync(scheduleId);
            if (s == null)
                return Json(new { success = false, message = "Schedule not found" });
            if (!string.Equals(s.CreatedBy, CurrentScheduleOwnerId(), StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "You can only view schedules you created." });

            return Json(new
            {
                success = true,
                schedule = new
                {
                    s.ScheduleId,
                    s.ReportType,
                    s.ScheduleName,
                    s.RecurrenceType,
                    s.RecurrenceDay,
                    scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                    s.ExportFormat,
                    s.Recipients,
                    s.EmailSubject,
                    s.IncludeAiAnalysis,
                    s.AiLocale,
                    s.SkipIfEmpty,
                    s.ParametersJson,
                    s.RecurrenceJson,
                    nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                    lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading schedule {ScheduleId}", scheduleId);
            return Json(new { success = false, message = "Failed to load schedule" });
        }
    }

    // Generic delete endpoint — works for ANY report type (AvgBasket, PS, Catalogue, BelowMinStock).
    // Soft-delete (sets IsActive = 0) via ScheduleRepository.DeleteScheduleAsync.
    [HttpPost]
    public async Task<IActionResult> DeleteSchedule(int scheduleId)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (scheduleId <= 0)
            return Json(new { success = false, message = "Invalid schedule id" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            // Ownership gate: fetch first so we can reject cross-user deletes.
            // Idempotent for same-user deletes — if already soft-deleted we still
            // return success = true so the UI can refresh.
            var existing = await repo.GetScheduleByIdAsync(scheduleId);
            if (existing == null)
                return Json(new { success = true, message = "Schedule not found or already deleted" });
            if (!string.Equals(existing.CreatedBy, CurrentScheduleOwnerId(), StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "You can only delete schedules you created." });

            var ok = await repo.DeleteScheduleAsync(scheduleId);
            return Json(new { success = ok, message = ok ? "Schedule deleted" : "Schedule not found or already deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting schedule {ScheduleId}", scheduleId);
            return Json(new { success = false, message = "Failed to delete schedule" });
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

    /// <summary>
    /// Truncate CSV to a safe size for inclusion in AI chat context.
    /// Keeps the metadata header block (lines starting with #) + first N data lines.
    /// gpt-4o-mini 128k context can handle ~50k chars comfortably; 200 rows covers
    /// virtually all real-world reports while keeping follow-up costs low.
    /// </summary>
    private static string TruncateCsvForChat(string csv, int maxDataRows = 200)
    {
        if (string.IsNullOrEmpty(csv)) return csv;

        var lines = csv.Split('\n');
        var sb = new StringBuilder();
        int dataCount = 0;
        bool truncated = false;

        foreach (var line in lines)
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line) || dataCount == 0)
            {
                sb.AppendLine(line.TrimEnd('\r'));
                if (!line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))
                    dataCount++;
            }
            else
            {
                dataCount++;
                if (dataCount <= maxDataRows + 1) // +1 for header row
                    sb.AppendLine(line.TrimEnd('\r'));
                else
                    truncated = true;
            }
        }

        if (truncated)
            sb.AppendLine($"# ... truncated to first {maxDataRows} data rows for chat context");

        return sb.ToString();
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
        string sortColumn = "Period", string sortDirection = "ASC", string? ItemsSelectionJson = null)
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
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
        string sortColumn = "Period", string sortDirection = "ASC", string? ItemsSelectionJson = null)
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
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
        string sortColumn = "Period", string sortDirection = "ASC", string? ItemsSelectionJson = null)
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
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
        string sortColumn = "Period", string sortDirection = "ASC", string? ItemsSelectionJson = null)
    {
        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy, includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
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
        string sortColumn = "Period", string sortDirection = "ASC", string? ItemsSelectionJson = null)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email format: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC email: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC email: {string.Join(", ", bccList.invalid)}" });

        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy,
            includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
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

        var selectionLines = new List<string>
        {
            $"Breakdown: {breakdown}",
            $"Group By: {groupBy}",
            $"Include VAT: {(includeVat ? "Yes" : "No")}"
        };
        if (secondaryGroupBy != GroupByType.None) selectionLines.Add($"Secondary Group: {secondaryGroupBy}");
        if (compareLastYear) selectionLines.Add("Compare Last Year: Yes");
        if (!string.IsNullOrWhiteSpace(storeCodes)) selectionLines.Add($"Stores: {storeCodes}");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

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
    <h4 style='color:#374151;margin:16px 0 8px;'>Selections for this report:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color: #6b7280; font-size: 13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";

        var selectionsText = string.Join("\n", selectionLines);
        var textBody = $"Average Basket Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var sentCount = 0;
        var errors = new List<string>();

        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
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
        string sortColumn, string sortDirection, string? itemsSelection = null)
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
            ItemsSelection = ParseItemsSelection(itemsSelection),
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
            if (parms.TryGetValue("ItemsSelectionJson", out var isj) && !string.IsNullOrEmpty(isj))
                model.ItemsSelectionJson = isj;
            if (parms.TryGetValue("ReportLayout", out var rl) && Enum.TryParse<ReportLayout>(rl, out var rlt))
                model.ReportLayout = rlt;
            if (parms.TryGetValue("ShowTotalQty", out var stq))
                model.ShowTotalQty = stq == "1";
            if (parms.TryGetValue("DatePreset", out var dp) && !string.IsNullOrEmpty(dp))
                model.DatePreset = dp;
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

    // ==================== Purchases vs Sales ====================

    public async Task<IActionResult> PurchasesSales()
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();

        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewPurchasesSales))
        {
            _logger.LogWarning("User {User} denied access to Purchases vs Sales (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewPurchasesSales);
            return RedirectToAction("AccessDenied", "Account");
        }

        // Purchases vs Sales inherently shows cost data — block if ViewCost right is denied.
        if (!CanViewCost())
        {
            _logger.LogWarning("User {User} denied Purchases vs Sales — ViewCost permission denied", User.Identity?.Name);
            return RedirectToAction("AccessDenied", "Account");
        }

        var viewModel = new PurchasesSalesViewModel
        {
            ConnectedDatabase = connectedDb,
            IsConnected = true,
            DateFrom = new DateTime(DateTime.Today.Year, 1, 1),
            DateTo = DateTime.Today,
            CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionSchedulePurchasesSales)
        };

        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();

        await ApplyPsSavedLayoutAsync(viewModel, tenantConnString);
        await LoadPsStoresAsync(viewModel, tenantConnString);
        await LoadPsFashionAvailabilityAsync(viewModel, tenantConnString);
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> PurchasesSales(PurchasesSalesViewModel model)
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();

        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewPurchasesSales))
            return RedirectToAction("AccessDenied", "Account");

        model.ConnectedDatabase = connectedDb;
        model.IsConnected = true;
        model.CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionSchedulePurchasesSales);
        await LoadPsStoresAsync(model, tenantConnString);
        await LoadPsFashionAvailabilityAsync(model, tenantConnString);

        var filter = model.ToPurchasesSalesFilter();
        filter.ItemsSelection = ParseItemsSelection(model.ItemsSelectionJson);
        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
        {
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;
        }

        if (filter.ItemsSelection?.Items.HasFilter == true
            && filter.ItemsSelection.Items.Mode == FilterMode.Include)
        {
            model.SelectedItemIds = filter.ItemsSelection.Items.Ids
                .Select(s => int.TryParse(s, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
        }

        // Cascade: groups must be filled in order (Primary→Secondary→Third)
        if (filter.PrimaryGroup == PsGroupBy.None)
        {
            filter.SecondaryGroup = PsGroupBy.None;
            filter.ThirdGroup = PsGroupBy.None;
        }
        if (filter.SecondaryGroup == PsGroupBy.None)
        {
            filter.ThirdGroup = PsGroupBy.None;
        }
        // Sync back to model so the view reflects the normalization
        model.PrimaryGroup = filter.PrimaryGroup;
        model.SecondaryGroup = filter.SecondaryGroup;
        model.ThirdGroup = filter.ThirdGroup;

        if (!filter.IsValid(out var errors))
        {
            model.ErrorMessage = string.Join(" ", errors);
            return View(model);
        }

        try
        {
            var repo = _repositoryFactory.CreatePurchasesSalesRepository(tenantConnString);

            if (filter.IsMonthly)
            {
                filter.ThirdGroup = PsGroupBy.None;
                var monthlyRows = await repo.GetPurchasesSalesMonthlyAsync(filter);
                model.MonthlyResults = monthlyRows;
                model.TotalCount = monthlyRows.Count;
            }
            else
            {
                var result = await repo.GetPurchasesSalesDataAsync(filter);
                model.Results = result.Items;
                model.TotalCount = result.TotalCount;
                model.PageNumber = result.PageNumber;
                model.PageSize = result.PageSize;
                model.Totals = result.PsTotals;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Purchases vs Sales report");
            model.ErrorMessage = "An error occurred while generating the report. Please try again.";
        }

        return View(model);
    }

    [HttpGet, HttpPost]
    public async Task<IActionResult> ExportPsExcel(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null) return RedirectToAction("PurchasesSales");

        var service = new ExcelExportService();
        var bytes = service.GeneratePurchasesSalesExcel(result.Value.rows, result.Value.totals, result.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet, HttpPost]
    public async Task<IActionResult> ExportPsPdf(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null) return RedirectToAction("PurchasesSales");

        var service = new PdfExportService();
        var bytes = service.GeneratePurchasesSalesPdf(result.Value.rows, result.Value.totals, result.Value.filter);
        return File(bytes, "application/pdf", $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet, HttpPost]
    public async Task<IActionResult> ExportPsCsv(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null) return RedirectToAction("PurchasesSales");

        var service = new CsvExportService();
        var bytes = service.GeneratePurchasesSalesCsv(result.Value.rows, result.Value.totals, result.Value.filter);
        return File(bytes, "text/csv", $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpPost]
    public async Task<IActionResult> SendPsReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GeneratePurchasesSalesPdf(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GeneratePurchasesSalesCsv(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GeneratePurchasesSalesExcel(result.Value.rows, result.Value.totals, result.Value.filter);
                fileName = $"PurchasesSales_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

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
            .Replace("\u00ABReportName\u00BB", "Purchases vs Sales")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", result.Value.rows.Count.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Purchases vs Sales Report — {period}")
            : emailSubject;

        var selectionLines = new List<string>
        {
            $"Mode: {reportMode}",
            $"Include VAT: {(includeVat ? "Yes" : "No")}"
        };
        if (primaryGroup != PsGroupBy.None) selectionLines.Add($"Primary Group: {primaryGroup}");
        if (secondaryGroup != PsGroupBy.None) selectionLines.Add($"Secondary Group: {secondaryGroup}");
        if (thirdGroup != PsGroupBy.None) selectionLines.Add($"Third Group: {thirdGroup}");
        if (showProfit) selectionLines.Add("Show Profit: Yes");
        if (showStock) selectionLines.Add("Show Stock: Yes");
        if (showOnOrder) selectionLines.Add("Show On Order: Yes");
        if (showReservation) selectionLines.Add("Show Reserved: Yes");
        if (showAvailable) selectionLines.Add("Show Available: Yes");
        if (!includeAdditionalCharges) selectionLines.Add("Cost: Wholesale only (excl. additional charges)");
        if (!string.IsNullOrWhiteSpace(storeCodes)) selectionLines.Add($"Stores: {storeCodes}");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family:Arial,sans-serif;max-width:600px;'>
    <h2 style='color:#2563eb;'>Purchases vs Sales Report</h2>
    <table style='border-collapse:collapse;width:100%;margin:16px 0;'>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Database</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Period</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Rows</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{result.Value.rows.Count}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <h4 style='color:#374151;margin:16px 0 8px;'>Selections for this report:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color:#6b7280;font-size:13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";
        var selectionsText = string.Join("\n", selectionLines);
        var textBody = $"Purchases vs Sales Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        var sentCount = 0;
        var sendErrors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send PS report email to {Email}", email);
                sendErrors.Add(email);
            }
        }

        if (sendErrors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", sendErrors)}" });

        var msg = sentCount == 1 ? $"Report sent to {emails[0]}" : $"Report sent to {sentCount} recipient(s)";
        if (sendErrors.Count > 0) msg += $" (failed: {string.Join(", ", sendErrors)})";
        return Json(new { success = true, message = msg });
    }

    private async Task<(List<PurchasesSalesRow> rows, PurchasesSalesTotals? totals, PurchasesSalesFilter filter)?> RunPsExportQuery(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn, string sortDirection,
        string? itemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new PurchasesSalesFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportMode = reportMode,
            PrimaryGroup = primaryGroup,
            SecondaryGroup = secondaryGroup,
            ThirdGroup = thirdGroup,
            IncludeVat = includeVat,
            ShowProfit = showProfit,
            ShowStock = showStock,
            ShowOnOrder = showOnOrder,
            ShowReservation = showReservation,
            ShowAvailable = showAvailable,
            IncludeAdditionalCharges = includeAdditionalCharges,
            StoreCodes = string.IsNullOrEmpty(storeCodes) ? new() : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            ItemIds = string.IsNullOrEmpty(itemIds) ? new() : itemIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out _)).Select(s => int.Parse(s.Trim())).ToList(),
            ItemsSelection = ParseItemsSelection(itemsSelectionJson),
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            PageSize = int.MaxValue
        };

        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
        {
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;
        }

        try
        {
            var repo = _repositoryFactory.CreatePurchasesSalesRepository(tenantConnString);
            var result = await repo.GetPurchasesSalesDataAsync(filter);
            return (result.Items, result.PsTotals, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting PS report");
            return null;
        }
    }

    private async Task LoadPsStoresAsync(PurchasesSalesViewModel model, string tenantConnString)
    {
        try
        {
            var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
            model.AvailableStores = await storeRepo.GetActiveStoresAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stores for PS report");
            model.AvailableStores = new();
        }
    }

    // Data-driven fashion dimensions: only surface Model/Colour/Size/GroupSize/Fabric/
    // Attribute filters when the tenant's items actually reference them.
    private async Task LoadPsFashionAvailabilityAsync(PurchasesSalesViewModel model, string tenantConnString)
    {
        try
        {
            var dimRepo = _repositoryFactory.CreateDimensionRepository(tenantConnString);
            var avail = await dimRepo.GetFashionDimensionAvailabilityAsync();
            model.ShowModels = avail.HasModels;
            model.ShowColours = avail.HasColours;
            model.ShowSizes = avail.HasSizes;
            model.ShowGroupSizes = avail.HasGroupSizes;
            model.ShowFabrics = avail.HasFabrics;
            model.ShowAttributes = avail.HasAttributes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load fashion dimension availability for PS report");
        }
    }

    private async Task ApplyPsSavedLayoutAsync(PurchasesSalesViewModel model, string tenantConnString)
    {
        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderPurchasesSales,
                userCode);

            if (parms.Count == 0) return;

            model.HasSavedLayout = true;

            if (parms.TryGetValue("IncludeVat", out var iv))
                model.IncludeVat = iv == "1";
            if (parms.TryGetValue("ShowProfit", out var sp))
                model.ShowProfit = sp == "1";
            if (parms.TryGetValue("ShowStock", out var ss))
                model.ShowStock = ss == "1";
            if (parms.TryGetValue("ShowOnOrder", out var soo))
                model.ShowOnOrder = soo == "1";
            if (parms.TryGetValue("ShowReservation", out var sres))
                model.ShowReservation = sres == "1";
            if (parms.TryGetValue("ShowAvailable", out var sav))
                model.ShowAvailable = sav == "1";
            if (parms.TryGetValue("IncludeAdditionalCharges", out var iac))
                model.IncludeAdditionalCharges = iac == "1";
            if (parms.TryGetValue("ReportMode", out var rm) && Enum.TryParse<PsReportMode>(rm, out var rmt))
                model.ReportMode = rmt;
            if (parms.TryGetValue("PrimaryGroup", out var pg) && Enum.TryParse<PsGroupBy>(pg, out var pgt))
                model.PrimaryGroup = pgt;
            if (parms.TryGetValue("SecondaryGroup", out var sg) && Enum.TryParse<PsGroupBy>(sg, out var sgt))
                model.SecondaryGroup = sgt;
            if (parms.TryGetValue("ThirdGroup", out var tg) && Enum.TryParse<PsGroupBy>(tg, out var tgt))
                model.ThirdGroup = tgt;
            if (parms.TryGetValue("PageSize", out var ps) && int.TryParse(ps, out var pageSize) && pageSize > 0)
                model.PageSize = pageSize;
            if (parms.TryGetValue("HiddenColumns", out var hc) && !string.IsNullOrEmpty(hc))
                model.HiddenColumns = hc;
            if (parms.TryGetValue("ItemsSelectionJson", out var isj) && !string.IsNullOrEmpty(isj))
                model.ItemsSelectionJson = isj;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load PS saved layout — using defaults");
        }
    }

    // ==================== PS Layout ====================

    [HttpPost]
    public async Task<IActionResult> SavePsLayout([FromBody] Dictionary<string, string> parameters)
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
                ModuleConstants.IniHeaderPurchasesSales,
                ModuleConstants.IniDescriptionPurchasesSales,
                userCode,
                parameters);

            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving PS layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetPsLayout()
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
                ModuleConstants.IniHeaderPurchasesSales,
                userCode);

            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting PS layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    // ==================== PS Schedules ====================

    [HttpGet]
    public async Task<IActionResult> GetPsSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.PurchasesSales);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                s.Recipients, s.ReportType,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SavePsSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionSchedulePurchasesSales))
            return Json(new { success = false, message = "You don't have permission to create schedules." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.PurchasesSales);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

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
                ReportType = ReportTypeConstants.PurchasesSales,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.PurchasesSales);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving PS report schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    // ==================== AI Report Analysis ====================

    [HttpGet]
    public IActionResult GetAiStatus()
    {
        return Json(new { configured = _analyzerFactory.IsConfigured });
    }

    // ==================== AI Prompt Templates ====================

    [HttpGet]
    public async Task<IActionResult> GetAiPromptTemplates(string? reportType = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var templates = await repo.GetAiPromptTemplatesAsync(reportType);
            return Json(templates.Select(t => new
            {
                templateId = t.TemplateId,
                templateName = t.TemplateName,
                reportType = t.ReportType,
                systemPrompt = t.SystemPrompt,
                isDefault = t.IsDefault
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveAiPromptTemplate([FromBody] AiPromptTemplateDto dto)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (dto == null || string.IsNullOrWhiteSpace(dto.TemplateName) || string.IsNullOrWhiteSpace(dto.SystemPrompt))
            return Json(new { success = false, message = "Template name and system prompt are required." });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var template = new AiPromptTemplate
            {
                TemplateName = dto.TemplateName,
                ReportType = string.IsNullOrWhiteSpace(dto.ReportType) ? null : dto.ReportType,
                SystemPrompt = dto.SystemPrompt,
                IsDefault = dto.IsDefault,
                CreatedBy = User.Identity?.Name ?? "Unknown"
            };

            if (dto.TemplateId > 0)
            {
                template.TemplateId = dto.TemplateId;
                await repo.UpdateAiPromptTemplateAsync(template);
                return Json(new { success = true, templateId = template.TemplateId, message = "Template updated." });
            }
            else
            {
                var id = await repo.CreateAiPromptTemplateAsync(template);
                return Json(new { success = true, templateId = id, message = "Template saved." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving AI prompt template");
            return Json(new { success = false, message = "Failed to save template. The table may not exist yet — run the SQL migration." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAiPromptTemplate(int templateId)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            await repo.DeleteAiPromptTemplateAsync(templateId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AI prompt template {Id}", templateId);
            return Json(new { success = false, message = ex.Message });
        }
    }

    public class AiPromptTemplateDto
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = "";
        public string? ReportType { get; set; }
        public string SystemPrompt { get; set; } = "";
        public bool IsDefault { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeAbReport(
        DateTime dateFrom, DateTime dateTo, BreakdownType breakdown, GroupByType groupBy,
        GroupByType secondaryGroupBy, bool includeVat, bool compareLastYear,
        string? storeCodes, string? itemIds,
        string sortColumn = "Period", string sortDirection = "ASC",
        string? locale = "el", int? promptTemplateId = null, string? ItemsSelectionJson = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunExportQuery(dateFrom, dateTo, breakdown, groupBy, secondaryGroupBy,
            includeVat, compareLastYear, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvService = new CsvExportService();
            var csvBytes = csvService.GenerateAverageBasketCsv(result.Value.rows, result.Value.totals, result.Value.filter);
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { /* fall through to default prompt */ }
                }
            }

            _logger.LogInformation(
                "AI analysis [AverageBasket]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "AverageBasket", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Average Basket report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzePsReport(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? locale = "el", int? promptTemplateId = null,
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvService = new CsvExportService();
            var csvBytes = csvService.GeneratePurchasesSalesCsv(result.Value.rows, result.Value.totals, result.Value.filter);
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { /* fall through to default prompt */ }
                }
            }

            _logger.LogInformation(
                "AI analysis [PurchasesSales]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "PurchasesSales", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Purchases vs Sales report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== AI Chat Follow-up ====================

    [HttpPost]
    public async Task<IActionResult> AiChatFollowup([FromBody] AiChatRequest request)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured." });

        if (request == null || string.IsNullOrWhiteSpace(request.Message))
            return Json(new { success = false, message = "Message is required." });

        if (request.History == null || request.History.Count == 0)
            return Json(new { success = false, message = "Conversation history is required." });

        const int maxHistoryMessages = 20;
        if (request.History.Count > maxHistoryMessages)
            return Json(new { success = false, message = $"Conversation too long (max {maxHistoryMessages} messages). Please start a new analysis." });

        try
        {
            var analyzer = _analyzerFactory.Create();
            var history = request.History
                .Select(m => new Services.AI.AiChatMessage(m.Role, m.Content))
                .ToList();

            var reply = await analyzer.ChatAsync(history, request.Message, HttpContext.RequestAborted);

            return Json(new
            {
                success = true,
                content = reply.Content,
                inputTokens = reply.InputTokens,
                outputTokens = reply.OutputTokens,
                durationMs = reply.DurationMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat follow-up failed");
            return Json(new { success = false, message = $"Chat failed: {ex.Message}" });
        }
    }

    public class AiChatRequest
    {
        public List<AiChatMessageDto> History { get; set; } = new();
        public string Message { get; set; } = "";
    }

    public class AiChatMessageDto
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    // ==================== PS Drill-Down ====================

    [HttpGet]
    public async Task<IActionResult> GetTransactionDetails(
        string itemCode, string type, DateTime dateFrom, DateTime dateTo, string? storeCodes)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(itemCode))
            return Json(new { success = false, message = "Item code is required" });

        var validTypes = new[] { "purchases", "sales", "all" };
        if (!validTypes.Contains(type?.ToLowerInvariant()))
            type = "all";

        var storeList = string.IsNullOrEmpty(storeCodes)
            ? null
            : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        try
        {
            var repo = _repositoryFactory.CreatePurchasesSalesRepository(tenantConnString);
            var details = await repo.GetTransactionDetailsAsync(itemCode, type!.ToLowerInvariant(), dateFrom, dateTo, storeList);

            return Json(new
            {
                success = true,
                count = details.Count,
                totalQty = details.Sum(d => d.Quantity),
                totalNet = details.Sum(d => d.NetAmount),
                totalGross = details.Sum(d => d.GrossAmount),
                rows = details.Select(d => new
                {
                    date = d.DateTrans.ToString("yyyy-MM-dd"),
                    d.Kind,
                    d.KindDescription,
                    doc = d.DocumentNumber,
                    d.EntityCode,
                    d.EntityName,
                    store = d.StoreCode,
                    d.ItemCode,
                    d.ItemName,
                    qty = d.Quantity,
                    price = d.UnitPrice,
                    discount = d.Discount,
                    net = d.NetAmount,
                    vat = d.VatAmount,
                    gross = d.GrossAmount
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction details for {ItemCode}", itemCode);
            return Json(new { success = false, message = "Failed to load transaction details." });
        }
    }

    // ==================== Catalogue: Item Stock Position ====================
    // Mirrors original Powersoft365 Item Stock Position dialog (toggled from Transaction History modal).

    [HttpGet]
    public async Task<IActionResult> GetItemStockPosition(string itemCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(itemCode))
            return Json(new { success = false, message = "Item code is required" });

        try
        {
            var repo = _repositoryFactory.CreateCatalogueRepository(tenantConnString);
            var result = await repo.GetItemStockPositionAsync(itemCode);

            return Json(new
            {
                success = true,
                itemCode = result.ItemCode,
                itemName = result.ItemName,
                count = result.Rows.Count,
                totalOnStock = result.TotalOnStock,
                totalOnTransfer = result.TotalOnTransfer,
                totalReserved = result.TotalReserved,
                totalOrdered = result.TotalOrdered,
                totalAvailable = result.TotalAvailable,
                rows = result.Rows.Select(r => new
                {
                    storeCode = r.StoreCode,
                    storeName = r.StoreName,
                    onStock = r.OnStock,
                    onTransfer = r.OnTransfer,
                    reserved = r.Reserved,
                    ordered = r.Ordered,
                    onWaybill = r.OnWaybill,
                    available = r.Available,
                    shelf = r.Shelf,
                    minStock = r.MinimumStock,
                    reqStock = r.RequiredStock
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching item stock position for {ItemCode}", itemCode);
            return Json(new { success = false, message = "Failed to load stock position." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDocumentDetail(string docType, string docNumber)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(docType) || string.IsNullOrWhiteSpace(docNumber))
            return Json(new { success = false, message = "Document type and number are required" });

        var validTypes = new[] { "P", "E", "I", "C" };
        if (!validTypes.Contains(docType))
            return Json(new { success = false, message = "Invalid document type" });

        try
        {
            var repo = _repositoryFactory.CreatePurchasesSalesRepository(tenantConnString);
            var doc = await repo.GetDocumentDetailAsync(docType, docNumber);

            if (doc == null)
                return Json(new { success = false, message = "Document not found" });

            return Json(new
            {
                success = true,
                doc = new
                {
                    doc.DocType,
                    doc.DocTypeDescription,
                    doc.DocumentNumber,
                    date = doc.DocumentDate.ToString("yyyy-MM-dd"),
                    doc.EntityCode,
                    doc.EntityName,
                    doc.StoreCode,
                    totalNet = doc.TotalNet,
                    totalVat = doc.TotalVat,
                    totalGross = doc.TotalGross,
                    lineCount = doc.Lines.Count,
                    lines = doc.Lines.Select(l => new
                    {
                        l.ItemCode,
                        l.ItemName,
                        qty = l.Quantity,
                        price = l.UnitPrice,
                        discount = l.Discount,
                        net = l.NetAmount,
                        vat = l.VatAmount,
                        gross = l.GrossAmount
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching document detail for {DocType} {DocNumber}", docType, docNumber);
            return Json(new { success = false, message = "Failed to load document details." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DocumentPreview(string docType, string docNumber)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Content("Not connected to database.");

        var validTypes = new[] { "P", "E", "I", "C" };
        if (string.IsNullOrWhiteSpace(docType) || !validTypes.Contains(docType) || string.IsNullOrWhiteSpace(docNumber))
            return Content("Invalid document type or number.");

        try
        {
            var repo = _repositoryFactory.CreatePurchasesSalesRepository(tenantConnString);
            var doc = await repo.GetDocumentDetailAsync(docType, docNumber);
            if (doc == null)
                return Content("Document not found.");

            return View(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading document preview for {DocType} {DocNumber}", docType, docNumber);
            return Content("Failed to load document.");
        }
    }

    // ==================== PS Print Preview ====================

    [HttpGet, HttpPost]
    public async Task<IActionResult> PrintPsPreview(
        DateTime dateFrom, DateTime dateTo, PsReportMode reportMode,
        PsGroupBy primaryGroup, PsGroupBy secondaryGroup, PsGroupBy thirdGroup,
        bool includeVat, bool showProfit, bool showStock,
        string? storeCodes, string? itemIds,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? ItemsSelectionJson = null,
        bool showOnOrder = false, bool showReservation = false,
        bool showAvailable = false, bool includeAdditionalCharges = true)
    {
        var result = await RunPsExportQuery(dateFrom, dateTo, reportMode, primaryGroup, secondaryGroup, thirdGroup,
            includeVat, showProfit, showStock, storeCodes, itemIds, sortColumn, sortDirection, ItemsSelectionJson,
            showOnOrder, showReservation, showAvailable, includeAdditionalCharges);
        if (result == null) return RedirectToAction("PurchasesSales");

        var model = new PurchasesSalesViewModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportMode = reportMode,
            PrimaryGroup = primaryGroup,
            SecondaryGroup = secondaryGroup,
            ThirdGroup = thirdGroup,
            IncludeVat = includeVat,
            ShowProfit = showProfit,
            ShowStock = showStock,
            ShowOnOrder = showOnOrder,
            ShowReservation = showReservation,
            ShowAvailable = showAvailable,
            IncludeAdditionalCharges = includeAdditionalCharges,
            ConnectedDatabase = GetConnectedDatabaseName(),
            Results = result.Value.rows,
            TotalCount = result.Value.rows.Count,
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

        return View(model);
    }

    // ==================== Pareto 80/20 ====================

    public async Task<IActionResult> Pareto()
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewPareto))
        {
            _logger.LogWarning("User {User} denied access to Pareto (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewPareto);
            return RedirectToAction("AccessDenied", "Account");
        }

        var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
        var stores = await storeRepo.GetActiveStoresAsync();

        ViewBag.ConnectedDatabase = connectedDb;
        ViewBag.Stores = stores;
        ViewBag.CanSchedule   = await IsActionAuthorizedAsync(ModuleConstants.ActionSchedulePareto);
        ViewBag.ViewCost      = CanViewCost();
        ViewBag.ViewSupplier  = CanViewSupplier();
        ViewBag.DateFrom = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        ViewBag.DateTo = DateTime.Today.ToString("yyyy-MM-dd");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetParetoData(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false,
        string? storeCodes = null,
        decimal classAThreshold = 80,
        decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        bool showOthers = false,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        string? itemsSelection = null,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10,
        int priceOnIndex = 0,
        bool priceOnIncludesVat = false)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        var filter = new ParetoFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Dimension = dimension,
            Metric = metric,
            IncludeVat = includeVat,
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ClassAThreshold = classAThreshold,
            ClassBThreshold = classBThreshold,
            ExcludeNegativeAmounts = excludeNegativeAmounts,
            ShowOthers = showOthers,
            ProfitBasis = profitBasis,
            ItemsSelection = ParseItemsSelection(itemsSelection),
            TimezoneOffsetMinutes = timezoneOffsetMinutes,
            PriceInterval = priceInterval,
            PriceOnIndex = priceOnIndex,
            PriceOnIncludesVat = priceOnIncludesVat
        };

        try
        {
            var repo = _repositoryFactory.CreateParetoRepository(tenantConnString);
            var result = await repo.GetParetoDataAsync(filter);
            return Json(new
            {
                success = true,
                rows = result.Rows,
                grandTotal = result.GrandTotal,
                totalQuantity = result.TotalQuantity,
                totalSubtotal = result.TotalSubtotal,
                totalProfit = result.TotalProfit,
                classACount = result.ClassACount,
                classBCount = result.ClassBCount,
                classCCount = result.ClassCCount,
                classAValue = result.ClassAValue,
                classBValue = result.ClassBValue,
                classCValue = result.ClassCValue,
                totalItems = result.Rows.Count,
                metric = metric.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Pareto data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ==================== Pareto Export ====================

    private async Task<ParetoResult?> RunParetoQuery(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension, ParetoMetric metric,
        bool includeVat, string? storeCodes,
        decimal classAThreshold, decimal classBThreshold,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10,
        int priceOnIndex = 0,
        bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
            classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
            timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat,
            itemsSelection);

        var repo = _repositoryFactory.CreateParetoRepository(tenantConnString);
        return await repo.GetParetoDataAsync(filter);
    }

    private ParetoFilter BuildParetoFilter(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension, ParetoMetric metric,
        bool includeVat, string? storeCodes,
        decimal classAThreshold, decimal classBThreshold,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10,
        int priceOnIndex = 0,
        bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        return new ParetoFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Dimension = dimension,
            Metric = metric,
            IncludeVat = includeVat,
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ClassAThreshold = classAThreshold,
            ClassBThreshold = classBThreshold,
            ExcludeNegativeAmounts = excludeNegativeAmounts,
            ProfitBasis = profitBasis,
            TimezoneOffsetMinutes = timezoneOffsetMinutes,
            PriceInterval = priceInterval,
            PriceOnIndex = priceOnIndex,
            PriceOnIncludesVat = priceOnIncludesVat,
            ItemsSelection = ParseItemsSelection(itemsSelection)
        };
    }

    [HttpGet]
    public async Task<IActionResult> ExportParetoExcel(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false, string? storeCodes = null,
        decimal classAThreshold = 80, decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10, int priceOnIndex = 0, bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        try
        {
            var result = await RunParetoQuery(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            if (result == null) return RedirectToAction("Pareto");

            var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            var service = new ExcelExportService();
            var bytes = service.GenerateParetoExcel(result, filter, CanViewCost());
            var filename = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Pareto Excel");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Pareto");
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportParetoPdf(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false, string? storeCodes = null,
        decimal classAThreshold = 80, decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10, int priceOnIndex = 0, bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        try
        {
            var result = await RunParetoQuery(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            if (result == null) return RedirectToAction("Pareto");

            var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            var service = new PdfExportService();
            var bytes = service.GenerateParetoPdf(result, filter, CanViewCost());
            var filename = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Pareto PDF");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Pareto");
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportParetoCsv(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false, string? storeCodes = null,
        decimal classAThreshold = 80, decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10, int priceOnIndex = 0, bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        try
        {
            var result = await RunParetoQuery(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            if (result == null) return RedirectToAction("Pareto");

            var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            var service = new CsvExportService();
            var bytes = service.GenerateParetoCsv(result, filter, CanViewCost());
            var filename = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Pareto CSV");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Pareto");
        }
    }

    // ==================== Pareto Schedule ====================

    [HttpGet]
    public async Task<IActionResult> GetParetoSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.Pareto);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                s.Recipients, s.ReportType,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveParetoSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionSchedulePareto))
            return Json(new { success = false, message = "You don't have permission to create schedules." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.Pareto);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

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
                ReportType = ReportTypeConstants.Pareto,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.Pareto);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Pareto report schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    // ==================== Pareto Email ====================

    [HttpPost]
    public async Task<IActionResult> SendParetoEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false, string? storeCodes = null,
        decimal classAThreshold = 80, decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10, int priceOnIndex = 0, bool priceOnIncludesVat = false,
        string? itemsSelection = null)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var result = await RunParetoQuery(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
            classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
            timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
            classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
            timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateParetoPdf(result, filter, CanViewCost());
                fileName = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateParetoCsv(result, filter, CanViewCost());
                fileName = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateParetoExcel(result, filter, CanViewCost());
                fileName = $"Pareto_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

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
            .Replace("\u00ABReportName\u00BB", "Pareto 80/20")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", result.Rows.Count.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Pareto 80/20 Report \u2014 {period}")
            : emailSubject;

        var selectionLines = new List<string>
        {
            $"Dimension: {dimension}",
            $"Metric: {metric}",
            $"Include VAT: {(includeVat ? "Yes" : "No")}",
            $"Exclude Negative: {(excludeNegativeAmounts ? "Yes" : "No")}",
            $"Class A Threshold: {classAThreshold}%"
        };
        if (metric == ParetoMetric.Profit) selectionLines.Add($"Profit Basis: {profitBasis}");
        if (!string.IsNullOrWhiteSpace(storeCodes)) selectionLines.Add($"Stores: {storeCodes}");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family:Arial,sans-serif;max-width:600px;'>
    <h2 style='color:#2563eb;'>Pareto 80/20 Report</h2>
    <table style='border-collapse:collapse;width:100%;margin:16px 0;'>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Database</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Period</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Items</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{result.Rows.Count}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <h4 style='color:#374151;margin:16px 0 8px;'>Selections for this report:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color:#6b7280;font-size:13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";
        var selectionsText = string.Join("\n", selectionLines);
        var textBody = $"Pareto 80/20 Report\nDatabase: {dbName}\nPeriod: {period}\nItems: {result.Rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        var sentCount = 0;
        var sendErrors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Pareto report email to {Email}", email);
                sendErrors.Add(email);
            }
        }

        if (sendErrors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", sendErrors)}" });

        var msg = sentCount == 1 ? $"Report sent to {emails[0]}" : $"Report sent to {sentCount} recipient(s)";
        if (sendErrors.Count > 0) msg += $" (failed: {string.Join(", ", sendErrors)})";
        return Json(new { success = true, message = msg });
    }

    // ==================== Pareto AI Analysis ====================

    [HttpPost]
    public async Task<IActionResult> AnalyzeParetoReport(
        DateTime dateFrom, DateTime dateTo,
        ParetoDimension dimension = ParetoDimension.Item,
        ParetoMetric metric = ParetoMetric.Value,
        bool includeVat = false, string? storeCodes = null,
        decimal classAThreshold = 80, decimal classBThreshold = 95,
        bool excludeNegativeAmounts = true,
        ParetoProfitBasis profitBasis = ParetoProfitBasis.LatestCost,
        int timezoneOffsetMinutes = 0,
        decimal priceInterval = 10, int priceOnIndex = 0, bool priceOnIncludesVat = false,
        string? itemsSelection = null,
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunParetoQuery(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
            classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
            timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvService = new CsvExportService();
            var filter = BuildParetoFilter(dateFrom, dateTo, dimension, metric, includeVat, storeCodes,
                classAThreshold, classBThreshold, excludeNegativeAmounts, profitBasis,
                timezoneOffsetMinutes, priceInterval, priceOnIndex, priceOnIncludesVat, itemsSelection);
            var csvBytes = csvService.GenerateParetoCsv(result, filter, CanViewCost());
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { /* fall through to default prompt */ }
                }
            }

            _logger.LogInformation(
                "AI analysis [Pareto]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Rows.Count, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "Pareto", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Pareto report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Pareto Layout ====================

    [HttpGet]
    public async Task<IActionResult> GetParetoLayout()
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
                ModuleConstants.IniHeaderPareto,
                userCode);

            return Json(new { success = true, hasSaved = parms.Count > 0, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Pareto layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveParetoLayout([FromBody] Dictionary<string, string> parameters)
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
                ModuleConstants.IniHeaderPareto,
                ModuleConstants.IniDescriptionPareto,
                userCode,
                parameters);

            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Pareto layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetParetoLayout()
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
                ModuleConstants.IniHeaderPareto,
                userCode);

            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting Pareto layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    // ==================== Charts ====================

    public async Task<IActionResult> Charts()
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCharts))
        {
            _logger.LogWarning("User {User} denied access to Charts (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewCharts);
            return RedirectToAction("AccessDenied", "Account");
        }

        var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
        var stores = await storeRepo.GetActiveStoresAsync();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCharts,
                userCode);
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { /* first time — no layout */ }

        ViewBag.ConnectedDatabase = connectedDb;
        ViewBag.Stores = stores;
        ViewBag.CanSchedule   = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCharts);
        ViewBag.ViewCost      = CanViewCost();
        ViewBag.ViewSupplier  = CanViewSupplier();
        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout = savedLayout;
        return View();
    }

    private async Task<List<ChartDataPoint>?> RunChartQuery(ChartFilter filter)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var repo = _repositoryFactory.CreateChartRepository(tenantConnString);
        return await repo.GetSalesBreakdownAsync(filter);
    }

    private ChartFilter BuildChartFilter(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension, ChartMetric metric,
        int topN, bool showOthers, bool compareLastYear, bool includeVat,
        string? storeCodes, string chartType,
        ChartMode mode = ChartMode.Sales,
        string? itemsSelection = null)
    {
        return new ChartFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Mode = mode,
            Dimension = dimension,
            Metric = metric,
            TopN = topN,
            ShowOthers = showOthers,
            CompareLastYear = compareLastYear,
            IncludeVat = includeVat,
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ChartType = chartType,
            ItemsSelection = ParseItemsSelection(itemsSelection)
        };
    }

    [HttpGet]
    public async Task<IActionResult> ExportChartExcel(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        ChartMode mode = ChartMode.Sales, string? itemsSelection = null)
    {
        try
        {
            var filter = BuildChartFilter(dateFrom, dateTo, dimension, metric, topN, showOthers, compareLastYear, includeVat, storeCodes, chartType, mode, itemsSelection);
            var data = await RunChartQuery(filter);
            if (data == null) return RedirectToAction("Charts");

            var service = new ExcelExportService();
            var bytes = service.GenerateChartExcel(data, filter);
            var filename = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Chart Excel");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Charts");
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportChartCsv(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        ChartMode mode = ChartMode.Sales, string? itemsSelection = null)
    {
        try
        {
            var filter = BuildChartFilter(dateFrom, dateTo, dimension, metric, topN, showOthers, compareLastYear, includeVat, storeCodes, chartType, mode, itemsSelection);
            var data = await RunChartQuery(filter);
            if (data == null) return RedirectToAction("Charts");

            var service = new CsvExportService();
            var bytes = service.GenerateChartCsv(data, filter);
            var filename = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Chart CSV");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Charts");
        }
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeChartReport(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string? itemsSelection = null,
        string? locale = "el", int? promptTemplateId = null,
        ChartMode mode = ChartMode.Sales)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        var filter = new ChartFilter
        {
            DateFrom = dateFrom, DateTo = dateTo,
            Mode = mode,
            Dimension = dimension, Metric = metric,
            TopN = topN, ShowOthers = showOthers,
            CompareLastYear = compareLastYear, IncludeVat = includeVat,
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ItemsSelection = ParseItemsSelection(itemsSelection)
        };

        try
        {
            var repo = _repositoryFactory.CreateChartRepository(tenantConnString);
            var data = await repo.GetSalesBreakdownAsync(filter);

            if (data == null || data.Count == 0)
                return Json(new { success = false, message = "No chart data to analyze. Please generate the chart first." });

            // Use the same rich CSV format as the export path — gives AI proper column labels
            // (e.g. "Last Year", "YoY %") instead of the old "CompareValue" bare header.
            var csvBytes = new CsvExportService().GenerateChartCsv(data, filter);
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                try
                {
                    var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
                    var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                    if (tpl != null) customPrompt = tpl.SystemPrompt;
                }
                catch { }
            }

            var reportContext = $"Charts ({dimension} by {metric}, Top {topN}, {dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}"
                + (compareLastYear ? ", with Last Year comparison" : "") + ")";

            _logger.LogInformation(
                "AI analysis [Chart]: {Dimension} x {Metric}, Top {TopN}, compareLY={CompareLY}, {DataPoints} points, {CsvLen} chars, user={User}",
                dimension, metric, topN, compareLastYear, data.Count, csvData.Length, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, reportContext, locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Chart report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetChartData(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        string? itemsSelection = null,
        ChartMode mode = ChartMode.Sales)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        var filter = new ChartFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Mode = mode,
            Dimension = dimension,
            Metric = metric,
            TopN = topN,
            ShowOthers = showOthers,
            CompareLastYear = compareLastYear,
            IncludeVat = includeVat,
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ChartType = chartType,
            ItemsSelection = ParseItemsSelection(itemsSelection)
        };

        try
        {
            var repo = _repositoryFactory.CreateChartRepository(tenantConnString);
            var data = await repo.GetSalesBreakdownAsync(filter);
            return Json(new { success = true, data, filter.ChartType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chart data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ==================== Charts PDF Export ====================

    [HttpGet]
    public async Task<IActionResult> ExportChartPdf(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        ChartMode mode = ChartMode.Sales, string? itemsSelection = null)
    {
        try
        {
            var filter = BuildChartFilter(dateFrom, dateTo, dimension, metric, topN, showOthers, compareLastYear, includeVat, storeCodes, chartType, mode, itemsSelection);
            var data = await RunChartQuery(filter);
            if (data == null) return RedirectToAction("Charts");

            var service = new PdfExportService();
            var bytes = service.GenerateChartPdf(data, filter);
            var filename = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Chart PDF");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Charts");
        }
    }

    // ==================== Charts Print Preview ====================

    [HttpGet]
    public async Task<IActionResult> PrintChartPreview(
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        ChartMode mode = ChartMode.Sales, string? itemsSelection = null)
    {
        try
        {
            var filter = BuildChartFilter(dateFrom, dateTo, dimension, metric, topN, showOthers, compareLastYear, includeVat, storeCodes, chartType, mode, itemsSelection);
            var data = await RunChartQuery(filter);
            if (data == null) return RedirectToAction("Charts");

            ViewBag.ChartData = data;
            ViewBag.ChartFilter = filter;
            ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
            return View("ChartPrintPreview");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Chart Print Preview");
            return Content($"<html><body><p>Error: {ex.Message}</p></body></html>", "text/html");
        }
    }

    // ==================== Charts Schedule ====================

    [HttpGet]
    public async Task<IActionResult> GetChartSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.Charts);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                s.Recipients, s.ReportType,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveChartSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCharts))
            return Json(new { success = false, message = "You don't have permission to create schedules." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.Charts);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

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
                ReportType = ReportTypeConstants.Charts,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.Charts);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Charts schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    // ==================== Charts Send Email ====================

    [HttpPost]
    public async Task<IActionResult> SendChartEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo,
        ChartDimension dimension = ChartDimension.Category,
        ChartMetric metric = ChartMetric.Value,
        int topN = 10, bool showOthers = true,
        bool compareLastYear = false, bool includeVat = false,
        string? storeCodes = null, string chartType = "pie",
        ChartMode mode = ChartMode.Sales, string? itemsSelection = null)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var filter = BuildChartFilter(dateFrom, dateTo, dimension, metric, topN, showOthers, compareLastYear, includeVat, storeCodes, chartType, mode, itemsSelection);
        var data = await RunChartQuery(filter);
        if (data == null || data.Count == 0)
            return Json(new { success = false, message = "Failed to generate chart data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateChartPdf(data, filter);
                fileName = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateChartCsv(data, filter);
                fileName = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateChartExcel(data, filter);
                fileName = $"Chart_{dimension}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

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
            .Replace("\u00ABReportName\u00BB", "Charts & Dashboards")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", data.Count.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Charts & Dashboards — {period}")
            : emailSubject;

        var selectionLines = new List<string>
        {
            $"Mode: {mode}",
            $"Dimension: {dimension}",
            $"Metric: {metric}",
            $"Top N: {topN}",
            $"Include VAT: {(includeVat ? "Yes" : "No")}"
        };
        if (compareLastYear) selectionLines.Add("Compare Last Year: Yes");
        if (!string.IsNullOrWhiteSpace(storeCodes)) selectionLines.Add($"Stores: {storeCodes}");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family: Arial, sans-serif; max-width: 600px;'>
    <h2 style='color: #2563eb;'>Charts & Dashboards Report</h2>
    <table style='border-collapse: collapse; width: 100%; margin: 16px 0;'>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Database</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Period</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Data Points</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{data.Count}</td></tr>
        <tr><td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb; color: #6b7280;'>Format</td>
            <td style='padding: 6px 12px; border-bottom: 1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <h4 style='color:#374151;margin:16px 0 8px;'>Chart Parameters:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color: #6b7280; font-size: 13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";

        var textBody = $"Charts & Dashboards Report\nDatabase: {dbName}\nPeriod: {period}\nData Points: {data.Count}\nFormat: {exportFormat}\n\nParameters:\n{string.Join("\n", selectionLines)}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var sentCount = 0;
        var errors = new List<string>();

        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send chart email to {Email}", email);
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

    // ==================== Charts Layout ====================

    [HttpPost]
    public async Task<IActionResult> SaveChartLayout([FromBody] Dictionary<string, string> parameters)
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
                ModuleConstants.IniHeaderCharts,
                ModuleConstants.IniDescriptionCharts,
                userCode,
                parameters);

            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving chart layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetChartLayout()
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
                ModuleConstants.IniHeaderCharts,
                userCode);

            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting chart layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    // ==================== Charts named/public layouts (multi per user) ====================

    [HttpGet]
    public async Task<IActionResult> ListChartLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCharts,
                userCode);

            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode,
                    name = l.Name,
                    isPublic = l.IsPublic,
                    createdBy = l.CreatedBy,
                    canEdit = l.CanEdit,
                    lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Chart layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to load layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveChartLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveChartLayoutAs([FromBody] SaveChartLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCharts,
                ModuleConstants.IniDescriptionCharts,
                userCode,
                req.Name,
                req.IsPublic,
                req.Parameters);

            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Chart named layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadChartLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });

            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Chart layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteChartLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Chart layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    // ==================== Power Reports Catalogue ====================

    public async Task<IActionResult> Catalogue()
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();

        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCatalogue))
        {
            _logger.LogWarning("User {User} denied access to Catalogue (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewCatalogue);
            return RedirectToAction("AccessDenied", "Account");
        }

        var viewModel = new CatalogueViewModel
        {
            ConnectedDatabase = connectedDb,
            IsConnected = true,
            DateFrom = new DateTime(DateTime.Today.Year, 1, 1),
            DateTo = DateTime.Today,
            CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCatalogue)
        };

        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();

        await LoadCatalogueStoresAsync(viewModel, tenantConnString);
        await LoadCatalogueSavedLayoutAsync(viewModel, tenantConnString);
        return View(viewModel);
    }

    private async Task LoadCatalogueSavedLayoutAsync(CatalogueViewModel model, string tenantConnString)
    {
        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCatalogue,
                userCode);

            if (parms.Count == 0) return;

            model.HasSavedLayout = true;

            if (parms.TryGetValue("DateBasis", out var db) && Enum.TryParse<CatalogueDateBasis>(db, out var dbt))
                model.DateBasis = dbt;
            if (parms.TryGetValue("UseDateTime", out var udt))
                model.UseDateTime = udt == "1";
            if (parms.TryGetValue("ReportMode", out var rm) && Enum.TryParse<CatalogueReportMode>(rm, out var rmt))
                model.ReportMode = rmt;
            if (parms.TryGetValue("ReportOn", out var ro) && Enum.TryParse<CatalogueReportOn>(ro, out var rot))
                model.ReportOn = rot;
            if (parms.TryGetValue("PrimaryGroup", out var pg) && Enum.TryParse<CatalogueGroupBy>(pg, out var pgt))
                model.PrimaryGroup = pgt;
            if (parms.TryGetValue("SecondaryGroup", out var sg) && Enum.TryParse<CatalogueGroupBy>(sg, out var sgt))
                model.SecondaryGroup = sgt;
            if (parms.TryGetValue("ThirdGroup", out var tg) && Enum.TryParse<CatalogueGroupBy>(tg, out var tgt))
                model.ThirdGroup = tgt;
            if (parms.TryGetValue("ProfitBasedOn", out var pbo) && Enum.TryParse<CatalogueCostBasis>(pbo, out var pbot))
                model.ProfitBasedOn = pbot;
            if (parms.TryGetValue("ProfitIncludesVat", out var piv))
                model.ProfitIncludesVat = piv == "1";
            if (parms.TryGetValue("StockValueBasedOn", out var svbo) && Enum.TryParse<CatalogueCostBasis>(svbo, out var svbot))
                model.StockValueBasedOn = svbot;
            if (parms.TryGetValue("StockValueIncludesVat", out var sviv))
                model.StockValueIncludesVat = sviv == "1";
            if (parms.TryGetValue("CostType", out var ct) && Enum.TryParse<CatalogueCostBasis>(ct, out var ctt))
                model.CostType = ctt;
            if (parms.TryGetValue("ShowProfit", out var sp))
                model.ShowProfit = sp == "1";
            if (parms.TryGetValue("ShowStock", out var ss))
                model.ShowStock = ss == "1";
            if (parms.TryGetValue("PageSize", out var ps) && int.TryParse(ps, out var pageSize) && pageSize > 0)
                model.PageSize = pageSize;
            if (parms.TryGetValue("DisplayColumns", out var dcs) && !string.IsNullOrEmpty(dcs))
                model.DisplayColumnsString = dcs;
            if (parms.TryGetValue("ColumnOrder", out var co) && !string.IsNullOrEmpty(co))
                model.ColumnOrder = co;
            if (parms.TryGetValue("ItemsSelectionJson", out var isj) && !string.IsNullOrEmpty(isj))
                model.ItemsSelectionJson = isj;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load Catalogue saved layout — using defaults");
        }
    }

    // ==================== Catalogue Layout (per-user saved defaults) ====================

    [HttpPost]
    public async Task<IActionResult> SaveCatalogueLayout([FromBody] Dictionary<string, string> parameters)
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
                ModuleConstants.IniHeaderCatalogue,
                ModuleConstants.IniDescriptionCatalogue,
                userCode,
                parameters);

            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Catalogue layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetCatalogueLayout()
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
                ModuleConstants.IniHeaderCatalogue,
                userCode);

            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting Catalogue layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    // ==================== Catalogue named/public layouts (multi per user) ====================

    /// <summary>
    /// Returns metadata for every Catalogue layout visible to the current user
    /// (own private layouts + all public layouts). Used by the layout picker dropdown.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListCatalogueLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCatalogue,
                userCode);

            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode,
                    name = l.Name,
                    isPublic = l.IsPublic,
                    createdBy = l.CreatedBy,
                    canEdit = l.CanEdit,
                    lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Catalogue layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveCatalogueLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Saves the form parameters as a NEW named layout (or overwrites an existing one with the
    /// same slug owned by the same scope). Public layouts have fk_UserCode = NULL and may only
    /// be overwritten/deleted by their original creator.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveCatalogueLayoutAs([FromBody] SaveCatalogueLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCatalogue,
                ModuleConstants.IniDescriptionCatalogue,
                userCode,
                req.Name,
                req.IsPublic,
                req.Parameters);

            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            // Authorisation: trying to overwrite a public layout owned by someone else.
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving named Catalogue layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    /// <summary>
    /// Loads a named Catalogue layout by its header code.
    /// Returns the raw parameter dictionary; the client applies it to the form.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LoadCatalogueLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });

            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Catalogue layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCatalogueLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var userCode = GetUserCode();
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, userCode);

            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Catalogue layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Catalogue(CatalogueViewModel model)
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();

        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCatalogue))
            return RedirectToAction("AccessDenied", "Account");

        model.ConnectedDatabase = connectedDb;
        model.IsConnected = true;
        model.CanSchedule = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCatalogue);
        await LoadCatalogueStoresAsync(model, tenantConnString);

        var filter = model.ToCatalogueFilter();
        filter.ItemsSelection = ParseItemsSelection(model.ItemsSelectionJson);
        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
        {
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;
        }

        if (filter.PrimaryGroup == CatalogueGroupBy.None)
        {
            filter.SecondaryGroup = CatalogueGroupBy.None;
            filter.ThirdGroup = CatalogueGroupBy.None;
        }
        if (filter.SecondaryGroup == CatalogueGroupBy.None)
        {
            filter.ThirdGroup = CatalogueGroupBy.None;
        }
        model.PrimaryGroup = filter.PrimaryGroup;
        model.SecondaryGroup = filter.SecondaryGroup;
        model.ThirdGroup = filter.ThirdGroup;

        if (!filter.IsValid(out var errors))
        {
            model.ErrorMessage = string.Join(" ", errors);
            return View(model);
        }

        try
        {
            var repo = _repositoryFactory.CreateCatalogueRepository(tenantConnString);

            var dataSw = Stopwatch.StartNew();
            var result = await repo.GetCatalogueDataAsync(filter);
            dataSw.Stop();

            model.Results = result.Items;
            model.TotalCount = result.TotalCount;
            model.PageNumber = result.PageNumber;
            model.PageSize = result.PageSize;

            var totalsSw = Stopwatch.StartNew();
            if (filter.ReportOn != CatalogueReportOn.Both)
            {
                var totals = await repo.GetCatalogueTotalsAsync(filter);
                model.Totals = totals;
            }
            totalsSw.Stop();

            // Performance instrumentation — see _DOCS/CATALOGUE_PRODUCTION_AUDIT.md §3.
            // Surface in app logs to detect slow queries per tenant/parameter combo.
            _logger.LogInformation(
                "Catalogue|TIMING db={Db} dateRange={From:yyyy-MM-dd}..{To:yyyy-MM-dd} mode={Mode} reportOn={ReportOn} groups=[{G1},{G2},{G3}] dataMs={DataMs} totalsMs={TotalsMs} rows={Rows}",
                connectedDb,
                filter.DateFrom, filter.DateTo,
                filter.ReportMode, filter.ReportOn,
                filter.PrimaryGroup, filter.SecondaryGroup, filter.ThirdGroup,
                dataSw.ElapsedMilliseconds, totalsSw.ElapsedMilliseconds,
                result.TotalCount);

            if (dataSw.ElapsedMilliseconds + totalsSw.ElapsedMilliseconds > 5000)
            {
                _logger.LogWarning(
                    "Catalogue|SLOW db={Db} totalMs={Total} — investigate index usage (see _SQL/INDEXES_CATALOGUE.sql)",
                    connectedDb, dataSw.ElapsedMilliseconds + totalsSw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Catalogue report");
            model.ErrorMessage = "An error occurred while generating the report. Please try again.";
        }

        return View(model);
    }

    private async Task LoadCatalogueStoresAsync(CatalogueViewModel model, string tenantConnString)
    {
        try
        {
            var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
            model.AvailableStores = await storeRepo.GetActiveStoresAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stores for Catalogue report");
            model.AvailableStores = new();
        }
    }

    // ==================== Catalogue Export ====================

    private async Task<(List<CatalogueRow> rows, CatalogueTotals? totals, CatalogueFilter filter)?>
        RunCatalogueExportQuery(CatalogueFilter filter)
    {
        if (!filter.IsValid(out var validationErrors))
        {
            _logger.LogWarning("Export/print requested with invalid filter: {Errors}", string.Join("; ", validationErrors));
            return null;
        }

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        try
        {
            var repo = _repositoryFactory.CreateCatalogueRepository(tenantConnString);
            filter.PageNumber = 1;
            filter.PageSize = int.MaxValue;
            var result = await repo.GetCatalogueDataAsync(filter);
            CatalogueTotals? totals = filter.ReportOn != CatalogueReportOn.Both
                ? await repo.GetCatalogueTotalsAsync(filter)
                : null;
            return (result.Items, totals, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Catalogue report");
            return null;
        }
    }

    private CatalogueFilter BuildCatalogueFilterFromParams(
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns, bool showProfit, bool showStock,
        string? storeCodes, string? itemsSelectionJson,
        string sortColumn, string sortDirection,
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        string? columnFilters = null)
    {
        var filter = new CatalogueFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            DateBasis = dateBasis,
            UseDateTime = useDateTime,
            ReportMode = reportMode,
            ReportOn = reportOn,
            PrimaryGroup = primaryGroup,
            SecondaryGroup = secondaryGroup,
            ThirdGroup = thirdGroup,
            ShowProfit = showProfit,
            ShowStock = showStock,
            StoreCodes = string.IsNullOrEmpty(storeCodes) ? new() : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            ItemsSelection = ParseItemsSelection(itemsSelectionJson),
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            PageSize = int.MaxValue
        };
        if (!string.IsNullOrWhiteSpace(displayColumns))
            filter.DisplayColumns = displayColumns.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (filter.PrimaryGroup == CatalogueGroupBy.None)
        {
            filter.SecondaryGroup = CatalogueGroupBy.None;
            filter.ThirdGroup = CatalogueGroupBy.None;
        }
        if (filter.SecondaryGroup == CatalogueGroupBy.None)
            filter.ThirdGroup = CatalogueGroupBy.None;

        if (filter.ItemsSelection != null && filter.ItemsSelection.Stores.HasFilter
            && filter.ItemsSelection.Stores.Mode == FilterMode.Include)
            filter.StoreCodes = filter.ItemsSelection.Stores.Ids;

        ApplyColumnFiltersFromJson(filter, columnFilters);

        // Permission enforcement (legacy action 6015 ViewCost): a user without cost
        // rights may only report on Sale. Purchase/Both expose cost, so force Sale and
        // disable profit regardless of what the client posted (defense vs crafted URLs).
        if (!CanViewCost())
        {
            filter.ReportOn = CatalogueReportOn.Sale;
            filter.ShowProfit = false;
        }
        // ViewSupplier (legacy action 1200): never group by Supplier when not allowed.
        if (!CanViewSupplier())
        {
            if (filter.PrimaryGroup == CatalogueGroupBy.Supplier) filter.PrimaryGroup = CatalogueGroupBy.None;
            if (filter.SecondaryGroup == CatalogueGroupBy.Supplier) filter.SecondaryGroup = CatalogueGroupBy.None;
            if (filter.ThirdGroup == CatalogueGroupBy.Supplier) filter.ThirdGroup = CatalogueGroupBy.None;
        }

        return filter;
    }

    private static void ApplyColumnFiltersFromJson(CatalogueFilter filter, string? columnFilters)
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
                    if (!string.IsNullOrEmpty(v))
                        filter.FilterValues[prop.Name] = v;
                }
            }
            if (doc.RootElement.TryGetProperty("operators", out var ops) && ops.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in ops.EnumerateObject())
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(v))
                        filter.FilterOperators[prop.Name] = v;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON — silently ignore, filters won't be applied.
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportCatalogueExcel(
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns,
        bool showProfit, bool showStock,
        string? storeCodes, string? itemsSelection,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        int profitBasedOn = 99, bool profitIncludesVat = false,
        int stockValueBasedOn = 99, bool stockValueIncludesVat = false,
        int costType = 99,
        string? columnFilters = null)
    {
        var filter = BuildCatalogueFilterFromParams(dateFrom, dateTo, reportMode, reportOn,
            primaryGroup, secondaryGroup, thirdGroup, displayColumns, showProfit, showStock,
            storeCodes, itemsSelection, sortColumn, sortDirection, dateBasis, useDateTime, columnFilters);
        filter.ProfitBasedOn = (CatalogueCostBasis)profitBasedOn;
        filter.ProfitIncludesVat = profitIncludesVat;
        filter.StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn;
        filter.StockValueIncludesVat = stockValueIncludesVat;
        filter.CostType = (CatalogueCostBasis)costType;

        var result = await RunCatalogueExportQuery(filter);
        if (result == null) return RedirectToAction("Catalogue");

        var bytes = new ExcelExportService().GenerateCatalogueExcel(result.Value.rows, result.Value.totals, result.Value.filter, CanViewCost(), CanViewSupplier());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCatalogueCsv(
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns,
        bool showProfit, bool showStock,
        string? storeCodes, string? itemsSelection,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        int profitBasedOn = 99, bool profitIncludesVat = false,
        int stockValueBasedOn = 99, bool stockValueIncludesVat = false,
        int costType = 99,
        string? columnFilters = null)
    {
        var filter = BuildCatalogueFilterFromParams(dateFrom, dateTo, reportMode, reportOn,
            primaryGroup, secondaryGroup, thirdGroup, displayColumns, showProfit, showStock,
            storeCodes, itemsSelection, sortColumn, sortDirection, dateBasis, useDateTime, columnFilters);
        filter.ProfitBasedOn = (CatalogueCostBasis)profitBasedOn;
        filter.ProfitIncludesVat = profitIncludesVat;
        filter.StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn;
        filter.StockValueIncludesVat = stockValueIncludesVat;
        filter.CostType = (CatalogueCostBasis)costType;

        var result = await RunCatalogueExportQuery(filter);
        if (result == null) return RedirectToAction("Catalogue");

        var bytes = new CsvExportService().GenerateCatalogueCsv(result.Value.rows, result.Value.totals, result.Value.filter, CanViewCost(), CanViewSupplier());
        return File(bytes, "text/csv", $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    // ==================== Catalogue Print Preview ====================

    [HttpGet]
    public async Task<IActionResult> PrintCataloguePreview(
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns,
        bool showProfit, bool showStock,
        int profitBasedOn = 99, bool profitIncludesVat = false,
        int stockValueBasedOn = 99, bool stockValueIncludesVat = false,
        int costType = 99,
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        string? columnFilters = null)
    {
        var filter = BuildCatalogueFilterFromParams(dateFrom, dateTo, reportMode, reportOn,
            primaryGroup, secondaryGroup, thirdGroup, displayColumns, showProfit, showStock,
            storeCodes, itemsSelection, sortColumn, sortDirection, dateBasis, useDateTime, columnFilters);

        filter.ProfitBasedOn = (CatalogueCostBasis)profitBasedOn;
        filter.ProfitIncludesVat = profitIncludesVat;
        filter.StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn;
        filter.StockValueIncludesVat = stockValueIncludesVat;
        filter.CostType = (CatalogueCostBasis)costType;

        var result = await RunCatalogueExportQuery(filter);
        if (result == null) return RedirectToAction("Catalogue");

        var model = new CatalogueViewModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            DateBasis = dateBasis,
            UseDateTime = useDateTime,
            ReportMode = reportMode,
            ReportOn = reportOn,
            PrimaryGroup = primaryGroup,
            SecondaryGroup = secondaryGroup,
            ThirdGroup = thirdGroup,
            ShowProfit = showProfit,
            ShowStock = showStock,
            ProfitBasedOn = (CatalogueCostBasis)profitBasedOn,
            ProfitIncludesVat = profitIncludesVat,
            StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn,
            StockValueIncludesVat = stockValueIncludesVat,
            CostType = (CatalogueCostBasis)costType,
            ConnectedDatabase = GetConnectedDatabaseName(),
            Results = result.Value.rows,
            TotalCount = result.Value.rows.Count,
            Totals = result.Value.totals,
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };
        if (!string.IsNullOrWhiteSpace(displayColumns))
            model.DisplayColumnsString = displayColumns;

        // Permission enforcement: strip cost/profit/margin + supplier columns server-side
        // so they never render in the printable output (JS hiding does not apply to print).
        model.DisplayColumnsString = StripRestrictedCatalogueColumns(model.DisplayColumnsString);

        return View(model);
    }

    // ==================== Catalogue Send Email ====================

    [HttpPost]
    public async Task<IActionResult> SendCatalogueEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns,
        bool showProfit, bool showStock,
        string? storeCodes, string? itemsSelection,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        int profitBasedOn = 99, bool profitIncludesVat = false,
        int stockValueBasedOn = 99, bool stockValueIncludesVat = false,
        int costType = 99,
        string? columnFilters = null)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var filter = BuildCatalogueFilterFromParams(dateFrom, dateTo, reportMode, reportOn,
            primaryGroup, secondaryGroup, thirdGroup, displayColumns, showProfit, showStock,
            storeCodes, itemsSelection, sortColumn, sortDirection, dateBasis, useDateTime, columnFilters);
        filter.ProfitBasedOn = (CatalogueCostBasis)profitBasedOn;
        filter.ProfitIncludesVat = profitIncludesVat;
        filter.StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn;
        filter.StockValueIncludesVat = stockValueIncludesVat;
        filter.CostType = (CatalogueCostBasis)costType;

        var result = await RunCatalogueExportQuery(filter);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "csv":
                fileBytes = new CsvExportService().GenerateCatalogueCsv(result.Value.rows, result.Value.totals, result.Value.filter, CanViewCost(), CanViewSupplier());
                fileName = $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            // TODO: implement PdfExportService.GenerateCataloguePdf — fall back to Excel for now
            case "pdf":
            default:
                fileBytes = new ExcelExportService().GenerateCatalogueExcel(result.Value.rows, result.Value.totals, result.Value.filter, CanViewCost(), CanViewSupplier());
                fileName = $"Catalogue_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";

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
            .Replace("\u00ABReportName\u00BB", "Power Reports Catalogue")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", result.Value.rows.Count.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Power Reports Catalogue — {period}")
            : emailSubject;

        var selectionLines = new List<string>
        {
            $"Report Mode: {reportMode}",
            $"Report On: {reportOn}"
        };
        if (primaryGroup != CatalogueGroupBy.None) selectionLines.Add($"Primary Group: {primaryGroup}");
        if (secondaryGroup != CatalogueGroupBy.None) selectionLines.Add($"Secondary Group: {secondaryGroup}");
        if (thirdGroup != CatalogueGroupBy.None) selectionLines.Add($"Third Group: {thirdGroup}");
        if (showProfit) selectionLines.Add("Show Profit: Yes");
        if (showStock) selectionLines.Add("Show Stock: Yes");
        if (!string.IsNullOrWhiteSpace(storeCodes)) selectionLines.Add($"Stores: {storeCodes}");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family:Arial,sans-serif;max-width:600px;'>
    <h2 style='color:#2563eb;'>Power Reports Catalogue</h2>
    <table style='border-collapse:collapse;width:100%;margin:16px 0;'>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Database</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Period</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Rows</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{result.Value.rows.Count}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <h4 style='color:#374151;margin:16px 0 8px;'>Selections for this report:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color:#6b7280;font-size:13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";
        var selectionsText = string.Join("\n", selectionLines);
        var textBody = $"Power Reports Catalogue\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        var sentCount = 0;
        var sendErrors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Catalogue report email to {Email}", email);
                sendErrors.Add(email);
            }
        }

        if (sendErrors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", sendErrors)}" });

        var msg = sentCount == 1 ? $"Report sent to {emails[0]}" : $"Report sent to {sentCount} recipient(s)";
        if (sendErrors.Count > 0) msg += $" (failed: {string.Join(", ", sendErrors)})";
        return Json(new { success = true, message = msg });
    }

    // ==================== Catalogue Schedules ====================

    [HttpGet]
    public async Task<IActionResult> GetCatalogueSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.Catalogue);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                s.Recipients, s.ReportType,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                lastRun = s.LastRunDate?.ToString("yyyy-MM-dd HH:mm")
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveCatalogueSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCatalogue))
            return Json(new { success = false, message = "You don't have permission to create schedules." });

        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.Catalogue);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

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
                ReportType = ReportTypeConstants.Catalogue,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.Catalogue);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Catalogue schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    // ==================== Catalogue AI Analysis ====================

    [HttpPost]
    public async Task<IActionResult> AnalyzeCatalogueReport(
        DateTime dateFrom, DateTime dateTo,
        CatalogueReportMode reportMode, CatalogueReportOn reportOn,
        CatalogueGroupBy primaryGroup, CatalogueGroupBy secondaryGroup, CatalogueGroupBy thirdGroup,
        string? displayColumns,
        bool showProfit, bool showStock,
        string? storeCodes, string? itemsSelection,
        string sortColumn = "ItemCode", string sortDirection = "ASC",
        string? locale = "el", int? promptTemplateId = null,
        CatalogueDateBasis dateBasis = CatalogueDateBasis.TransactionDate,
        bool useDateTime = false,
        int profitBasedOn = 99, bool profitIncludesVat = false,
        int stockValueBasedOn = 99, bool stockValueIncludesVat = false,
        int costType = 99,
        string? columnFilters = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var filter = BuildCatalogueFilterFromParams(dateFrom, dateTo, reportMode, reportOn,
            primaryGroup, secondaryGroup, thirdGroup, displayColumns, showProfit, showStock,
            storeCodes, itemsSelection, sortColumn, sortDirection, dateBasis, useDateTime, columnFilters);
        filter.ProfitBasedOn = (CatalogueCostBasis)profitBasedOn;
        filter.ProfitIncludesVat = profitIncludesVat;
        filter.StockValueBasedOn = (CatalogueCostBasis)stockValueBasedOn;
        filter.StockValueIncludesVat = stockValueIncludesVat;
        filter.CostType = (CatalogueCostBasis)costType;

        var result = await RunCatalogueExportQuery(filter);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvBytes = new CsvExportService().GenerateCatalogueCsv(result.Value.rows, result.Value.totals, result.Value.filter, CanViewCost(), CanViewSupplier());
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { /* fall through to default prompt */ }
                }
            }

            _logger.LogInformation(
                "AI analysis [Catalogue]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "Catalogue", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Catalogue report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Below Minimum Stock ====================

    public async Task<IActionResult> BelowMinStock()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewBelowMinStock))
        {
            _logger.LogWarning("User {User} denied access to BelowMinStock (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewBelowMinStock);
            return RedirectToAction("AccessDenied", "Account");
        }

        var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
        var stores = await storeRepo.GetActiveStoresAsync();
        ViewBag.StoresJson = System.Text.Json.JsonSerializer.Serialize(
            stores.Select(s => new { code = s.StoreCode, name = s.StoreName }));
        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
        ViewBag.CanSchedule  = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleBelowMinStock);
        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetBelowMinStockData(
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = new BelowMinStockFilter
            {
                StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                    : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ItemsSelection = ParseItemsSelection(itemsSelection),
                SortColumn = sortColumn,
                SortDirection = sortDirection
            };

            var repo = _repositoryFactory.CreateBelowMinStockRepository(tenantConnString);
            var data = await repo.GetBelowMinStockAsync(filter);

            return Json(new { success = true, data, totalRows = data.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading below-minimum stock data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveBmsSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleBelowMinStock))
            return Json(new { success = false, message = "Not authorized to schedule this report." });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var parsedTime = TimeSpan.TryParse(scheduleTime, out var ts) ? ts : new TimeSpan(8, 0, 0);
            DateTime? nextRun = null;

            if (!string.IsNullOrWhiteSpace(recurrenceJson))
                nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
            if (nextRun == null)
                nextRun = CalculateNextRun(recurrenceType ?? "Daily", recurrenceDay, parsedTime);

            var schedule = new ReportSchedule
            {
                ReportType = ReportTypeConstants.BelowMinStock,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.BelowMinStock);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving BelowMinStock schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBmsSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.BelowMinStock);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                s.SkipIfEmpty
            }));
        }
        catch { return Json(Array.Empty<object>()); }
    }

    // ==================== Cancellation Logging ====================

    public async Task<IActionResult> CancelLog()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCancelLog))
        {
            _logger.LogWarning("User {User} denied access to CancelLog (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewCancelLog);
            return RedirectToAction("AccessDenied", "Account");
        }

        var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
        var stores = await storeRepo.GetActiveStoresAsync();
        ViewBag.StoresJson = System.Text.Json.JsonSerializer.Serialize(
            stores.Select(s => new { code = s.StoreCode, name = s.StoreName }));
        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCancelLog,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { /* first time — no layout */ }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout    = savedLayout;
        ViewBag.CanSchedule    = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCancelLog);
        ViewBag.ViewCost       = CanViewCost();
        ViewBag.ViewSupplier   = CanViewSupplier();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetCancelLogData(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string? itemsSelection = null,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = new CancelLogFilter
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                ReportByDateTime = reportByDateTime,
                ActionType = Enum.TryParse<CancelLogActionType>(actionType, true, out var at) ? at : CancelLogActionType.All,
                ReportType = Enum.TryParse<CancelLogReportType>(reportType, true, out var rt) ? rt : CancelLogReportType.Detailed,
                PrimaryGroup = primaryGroup ?? "NONE",
                SecondaryGroup = secondaryGroup ?? "NONE",
                TimezoneOffsetMinutes = timezoneOffsetMinutes,
                MaxRecords = maxRecords,
                ItemsSelection = ParseItemsSelection(itemsSelection),
                SortColumn = sortColumn,
                SortDirection = sortDirection
            };

            var repo = _repositoryFactory.CreateCancelLogRepository(tenantConnString);

            if (filter.ReportType == CancelLogReportType.Summary)
            {
                var (rows, totalRecords) = await repo.GetSummaryAsync(filter);
                return Json(new { success = true, data = rows, totalRows = rows.Count, totalRecords, reportType = "Summary" });
            }
            else
            {
                var (rows, totalRecords) = await repo.GetDetailedAsync(filter);
                return Json(new { success = true, data = rows, totalRows = rows.Count, totalRecords, reportType = "Detailed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading cancellation log data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveCancelLogSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCancelLog))
            return Json(new { success = false, message = "Not authorized to schedule this report." });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            var parsedTime = TimeSpan.TryParse(scheduleTime, out var ts) ? ts : new TimeSpan(8, 0, 0);
            DateTime? nextRun = null;

            if (string.Equals(recurrenceType, "Once", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(recurrenceJson))
                {
                    nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
                    if (nextRun == null)
                        return Json(new { success = false, message = "For 'Run once', please set a valid start date and time in the future." });
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
                ReportType = ReportTypeConstants.CancelLog,
                ScheduleName = scheduleName,
                CreatedBy = User.Identity?.Name ?? "unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay = recurrenceDay,
                ScheduleTime = parsedTime,
                ExportFormat = exportFormat ?? "Excel",
                Recipients = recipients,
                EmailSubject = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(parametersJson),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale = aiLocale ?? "el",
                SkipIfEmpty = skipIfEmpty,
                IsActive = true
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.CancelLog);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving cancel log schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCancelLogSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.CancelLog);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                s.SkipIfEmpty
            }));
        }
        catch { return Json(Array.Empty<object>()); }
    }

    // ==================== CancelLog Layout (per-user saved defaults) ====================

    [HttpPost]
    public async Task<IActionResult> SaveCancelLogLayout([FromBody] Dictionary<string, string> parameters)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (parameters == null || parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            await repo.SaveLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCancelLog,
                ModuleConstants.IniDescriptionCancelLog,
                GetUserCode(),
                parameters);
            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving CancelLog layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetCancelLogLayout()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCancelLog,
                GetUserCode());
            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting CancelLog layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListCancelLogLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCancelLog,
                GetUserCode());
            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode, name = l.Name, isPublic = l.IsPublic,
                    createdBy = l.CreatedBy, canEdit = l.CanEdit, lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing CancelLog layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveCancelLogLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveCancelLogLayoutAs([FromBody] SaveCancelLogLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderCancelLog,
                ModuleConstants.IniDescriptionCancelLog,
                GetUserCode(),
                req.Name, req.IsPublic, req.Parameters);
            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving named CancelLog layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadCancelLogLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });
            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CancelLog layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCancelLogLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting CancelLog layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    // ==================== CancelLog Export / Email / AI ====================

    private async Task<(List<CancelLogDetailedRow>? detailed, List<CancelLogSummaryRow>? summary, CancelLogFilter filter)?> RunCancelLogQuery(
        DateTime dateFrom, DateTime dateTo, bool reportByDateTime, string actionType, string reportType,
        string primaryGroup, string secondaryGroup, int timezoneOffsetMinutes, int maxRecords,
        string sortColumn, string sortDirection, string? itemsSelectionJson)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new CancelLogFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportByDateTime = reportByDateTime,
            ActionType = Enum.TryParse<CancelLogActionType>(actionType, true, out var at) ? at : CancelLogActionType.All,
            ReportType = Enum.TryParse<CancelLogReportType>(reportType, true, out var rt) ? rt : CancelLogReportType.Detailed,
            PrimaryGroup = primaryGroup ?? "NONE",
            SecondaryGroup = secondaryGroup ?? "NONE",
            TimezoneOffsetMinutes = timezoneOffsetMinutes,
            MaxRecords = maxRecords,
            ItemsSelection = ParseItemsSelection(itemsSelectionJson),
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

        try
        {
            var repo = _repositoryFactory.CreateCancelLogRepository(tenantConnString);

            if (filter.ReportType == CancelLogReportType.Summary)
            {
                var (rows, _) = await repo.GetSummaryAsync(filter);
                return (null, rows, filter);
            }
            else
            {
                var (rows, _) = await repo.GetDetailedAsync(filter);
                return (rows, null, filter);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting CancelLog report");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportCancelLogCsv(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null)
    {
        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null) return RedirectToAction("CancelLog");

        var service = new CsvExportService();
        var bytes = service.GenerateCancelLogCsv(result.Value.detailed, result.Value.summary, result.Value.filter);
        return File(bytes, "text/csv", $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCancelLogExcel(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null)
    {
        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null) return RedirectToAction("CancelLog");

        var service = new ExcelExportService();
        var bytes = service.GenerateCancelLogExcel(result.Value.detailed, result.Value.summary, result.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCancelLogPdf(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null)
    {
        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null) return RedirectToAction("CancelLog");

        var service = new PdfExportService();
        var bytes = service.GenerateCancelLogPdf(result.Value.detailed, result.Value.summary, result.Value.filter);
        return File(bytes, "application/pdf", $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> CancelLogPrintPreview(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null)
    {
        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null) return RedirectToAction("CancelLog");

        var model = new ViewModels.CancelLogViewModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportByDateTime = reportByDateTime,
            ActionType = actionType,
            ReportType = reportType,
            PrimaryGroup = primaryGroup,
            SecondaryGroup = secondaryGroup,
            TimezoneOffsetMinutes = timezoneOffsetMinutes,
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            ConnectedDatabase = GetConnectedDatabaseName(),
            DetailedRows = result.Value.detailed ?? new(),
            SummaryRows = result.Value.summary ?? new(),
            TotalCount = (result.Value.detailed?.Count ?? 0) + (result.Value.summary?.Count ?? 0)
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SendCancelLogReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateCancelLogPdf(result.Value.detailed, result.Value.summary, result.Value.filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateCancelLogCsv(result.Value.detailed, result.Value.summary, result.Value.filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateCancelLogExcel(result.Value.detailed, result.Value.summary, result.Value.filter);
                fileName = $"CancelLog_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        var rowCount = result.Value.detailed?.Count ?? result.Value.summary?.Count ?? 0;

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
            .Replace("\u00ABReportName\u00BB", "Cancel Log")
            .Replace("\u00ABDatabaseName\u00BB", dbName)
            .Replace("\u00ABPeriod\u00BB", period)
            .Replace("\u00ABRowCount\u00BB", rowCount.ToString())
            .Replace("\u00ABExportFormat\u00BB", exportFormat ?? "Excel")
            .Replace("\u00ABGeneratedDate\u00BB", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("\u00ABUserName\u00BB", userName)
            .Replace("\u00ABCompanyName\u00BB", dbName)
            .Replace("\u00AB", "").Replace("\u00BB", "");

        var subject = string.IsNullOrWhiteSpace(emailSubject)
            ? (templateSubject != null ? ReplaceMergeFields(templateSubject) : $"Cancel Log Report \u2014 {period}")
            : emailSubject;

        var selectionLines = new List<string>
        {
            $"Action Type: {actionType}",
            $"Report Type: {reportType}"
        };
        if (primaryGroup != "NONE") selectionLines.Add($"Primary Group: {primaryGroup}");
        if (secondaryGroup != "NONE") selectionLines.Add($"Secondary Group: {secondaryGroup}");
        if (reportByDateTime) selectionLines.Add("Report by DateTime: Yes");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody)
            ? ReplaceMergeFields(templateBody)
            : $@"
<div style='font-family:Arial,sans-serif;max-width:600px;'>
    <h2 style='color:#2563eb;'>Cancel Log Report</h2>
    <table style='border-collapse:collapse;width:100%;margin:16px 0;'>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Database</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'><strong>{dbName}</strong></td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Period</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{period}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Rows</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{rowCount}</td></tr>
        <tr><td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
            <td style='padding:6px 12px;border-bottom:1px solid #e5e7eb;'>{exportFormat}</td></tr>
    </table>
    <h4 style='color:#374151;margin:16px 0 8px;'>Selections for this report:</h4>
    <table style='border-collapse:collapse;width:100%;margin:0 0 16px;'>{selectionsHtml}</table>
    <p style='color:#6b7280;font-size:13px;'>Sent by {userName} via Powersoft Reporting Engine.</p>
</div>";
        var selectionsText = string.Join("\n", selectionLines);
        var textBody = $"Cancel Log Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {rowCount}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        var sentCount = 0;
        var sendErrors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send CancelLog report email to {Email}", email);
                sendErrors.Add(email);
            }
        }

        if (sendErrors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", sendErrors)}" });

        var msg = sentCount == 1 ? $"Report sent to {emails[0]}" : $"Report sent to {sentCount} recipient(s)";
        if (sendErrors.Count > 0) msg += $" (failed: {string.Join(", ", sendErrors)})";
        return Json(new { success = true, message = msg });
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeCancelLogReport(
        DateTime dateFrom, DateTime dateTo,
        bool reportByDateTime = false,
        string actionType = "All",
        string reportType = "Detailed",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int timezoneOffsetMinutes = 0,
        int maxRecords = 50000,
        string sortColumn = "SessionDateTime",
        string sortDirection = "ASC",
        string? itemsSelectionJson = null,
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunCancelLogQuery(dateFrom, dateTo, reportByDateTime, actionType, reportType,
            primaryGroup, secondaryGroup, timezoneOffsetMinutes, maxRecords,
            sortColumn, sortDirection, itemsSelectionJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        var rowCount = result.Value.detailed?.Count ?? result.Value.summary?.Count ?? 0;
        if (rowCount == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvService = new CsvExportService();
            var csvBytes = csvService.GenerateCancelLogCsv(result.Value.detailed, result.Value.summary, result.Value.filter);
            var csvData = System.Text.Encoding.UTF8.GetString(csvBytes);

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { /* fall through to default prompt */ }
                }
            }

            _logger.LogInformation(
                "AI analysis [CancelLog]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                rowCount, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "CancelLog", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Cancel Log report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Prospect Clients ====================

    public async Task<IActionResult> ProspectClients()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewProspectClients))
        {
            _logger.LogWarning("User {User} denied access to Prospect Clients (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewProspectClients);
            return RedirectToAction("AccessDenied", "Account");
        }

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProspectClients,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout = savedLayout;

        var extraFieldLabels = new Dictionary<string, string>();
        try
        {
            var repo = _repositoryFactory.CreateProspectClientsRepository(tenantConnString);
            extraFieldLabels = await repo.GetExtraFieldLabelsAsync();
        }
        catch { }
        ViewBag.ExtraFieldLabels = extraFieldLabels;
        ViewBag.CanSchedule      = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleProspectClients);
        ViewBag.ViewCost         = CanViewCost();
        ViewBag.ViewSupplier     = CanViewSupplier();

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetProspectClientsLookups()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { agents = Array.Empty<object>(), categories1 = Array.Empty<object>(), categories2 = Array.Empty<object>() });

        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
            await conn.OpenAsync();

            var agents = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DISTINCT a.pk_SystemNo, ISNULL(a.FirstName,'') + ' ' + ISNULL(a.LastName,'') AS AgentName " +
                "FROM tbl_Agent a INNER JOIN tbl_Lead t ON t.fk_FollowedById = a.pk_SystemNo " +
                "ORDER BY AgentName", conn))
            {
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    agents.Add(new { id = rdr.GetInt64(0).ToString(), name = rdr.GetString(1).Trim() });
            }

            var categories1 = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DISTINCT c.pk_CategoryID, c.CategoryDescr " +
                "FROM tbl_CustCategory c INNER JOIN tbl_Lead t ON t.fk_Category1 = c.pk_CategoryID " +
                "ORDER BY c.CategoryDescr", conn))
            {
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    categories1.Add(new { id = rdr.GetInt64(0).ToString(), name = (rdr.IsDBNull(1) ? "" : rdr.GetString(1)).Trim() });
            }

            var categories2 = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DISTINCT c.pk_CategoryID, c.CategoryDescr " +
                "FROM tbl_CustCategory c INNER JOIN tbl_Lead t ON t.fk_Category2 = c.pk_CategoryID " +
                "ORDER BY c.CategoryDescr", conn))
            {
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    categories2.Add(new { id = rdr.GetInt64(0).ToString(), name = (rdr.IsDBNull(1) ? "" : rdr.GetString(1)).Trim() });
            }

            return Json(new { agents, categories1, categories2 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading prospect clients lookups");
            return Json(new { agents = Array.Empty<object>(), categories1 = Array.Empty<object>(), categories2 = Array.Empty<object>() });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetProspectClientsData(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All",
        string priorityFilter = "All",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int maxRecords = 50000,
        string sortColumn = "RegistrationDate",
        string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All",
        string category1Filter = "All",
        string category2Filter = "All",
        string customerCodesJson = "",
        bool customerExcludeMode = false,
        string statusCodesJson = "",
        string priorityCodesJson = "",
        string category1CodesJson = "",
        string category2CodesJson = "")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = new ProspectClientsFilter
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                DateField = dateField,
                StatusFilter = statusFilter,
                PriorityFilter = priorityFilter,
                PrimaryGroup = primaryGroup ?? "NONE",
                SecondaryGroup = secondaryGroup ?? "NONE",
                MaxRecords = maxRecords,
                SortColumn = sortColumn,
                SortDirection = sortDirection,
                IncludeHistory = includeHistory,
                FollowedByFilter = followedByFilter,
                Category1Filter = category1Filter,
                Category2Filter = category2Filter,
                CustomerCodes = ParseCustomerCodesJson(customerCodesJson),
                CustomerExcludeMode = customerExcludeMode,
                StatusCodes = ParseCustomerCodesJson(statusCodesJson),
                PriorityCodes = ParseCustomerCodesJson(priorityCodesJson),
                Category1Codes = ParseCustomerCodesJson(category1CodesJson),
                Category2Codes = ParseCustomerCodesJson(category2CodesJson)
            };

            var repo = _repositoryFactory.CreateProspectClientsRepository(tenantConnString);
            var (rows, totalRecords) = await repo.GetDataAsync(filter);
            return Json(new { success = true, data = rows, totalRows = rows.Count, totalRecords });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading prospect clients data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // --- Prospect Clients Layout CRUD ---

    [HttpPost]
    public async Task<IActionResult> SaveProspectClientsLayout([FromBody] Dictionary<string, string> parameters)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (parameters == null || parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            await repo.SaveLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProspectClients,
                ModuleConstants.IniDescriptionProspectClients,
                GetUserCode(),
                parameters);
            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving ProspectClients layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetProspectClientsLayout()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProspectClients,
                GetUserCode());
            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting ProspectClients layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListProspectClientsLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProspectClients,
                GetUserCode());
            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode, name = l.Name, isPublic = l.IsPublic,
                    createdBy = l.CreatedBy, canEdit = l.CanEdit, lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing ProspectClients layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveProspectClientsLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveProspectClientsLayoutAs([FromBody] SaveProspectClientsLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProspectClients,
                ModuleConstants.IniDescriptionProspectClients,
                GetUserCode(),
                req.Name, req.IsPublic, req.Parameters);
            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving named ProspectClients layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadProspectClientsLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });
            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ProspectClients layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProspectClientsLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting ProspectClients layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    // --- Prospect Clients Export / Email / AI ---

    private async Task<(List<ProspectClientsRow> rows, ProspectClientsFilter filter)?> RunProspectClientsQuery(
        DateTime dateFrom, DateTime dateTo, string dateField, string statusFilter, string priorityFilter,
        string primaryGroup, string secondaryGroup, int maxRecords,
        string sortColumn, string sortDirection, bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new ProspectClientsFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            DateField = dateField,
            StatusFilter = statusFilter,
            PriorityFilter = priorityFilter,
            PrimaryGroup = primaryGroup ?? "NONE",
            SecondaryGroup = secondaryGroup ?? "NONE",
            MaxRecords = maxRecords,
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            IncludeHistory = includeHistory,
            FollowedByFilter = followedByFilter,
            Category1Filter = category1Filter,
            Category2Filter = category2Filter,
            CustomerCodes = ParseCustomerCodesJson(customerCodesJson),
            CustomerExcludeMode = customerExcludeMode,
            StatusCodes = ParseCustomerCodesJson(statusCodesJson),
            PriorityCodes = ParseCustomerCodesJson(priorityCodesJson),
            Category1Codes = ParseCustomerCodesJson(category1CodesJson),
            Category2Codes = ParseCustomerCodesJson(category2CodesJson)
        };

        try
        {
            var repo = _repositoryFactory.CreateProspectClientsRepository(tenantConnString);
            var (rows, _) = await repo.GetDataAsync(filter);
            return (rows, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running ProspectClients query");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportProspectClientsCsv(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null) return RedirectToAction("ProspectClients");

        var bytes = new CsvExportService().GenerateProspectClientsCsv(result.Value.rows, result.Value.filter);
        return File(bytes, "text/csv", $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportProspectClientsExcel(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null) return RedirectToAction("ProspectClients");

        var bytes = new ExcelExportService().GenerateProspectClientsExcel(result.Value.rows, result.Value.filter);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportProspectClientsPdf(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null) return RedirectToAction("ProspectClients");

        var bytes = new PdfExportService().GenerateProspectClientsPdf(result.Value.rows, result.Value.filter);
        return File(bytes, "application/pdf",
            $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> ProspectClientsPrintPreview(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null) return RedirectToAction("ProspectClients");

        ViewBag.Rows = result.Value.rows;
        ViewBag.Filter = result.Value.filter;
        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
        return View("ProspectClientsPrintPreview");
    }

    [HttpPost]
    public async Task<IActionResult> SendProspectClientsReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat,
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateProspectClientsPdf(result.Value.rows, result.Value.filter);
                fileName = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateProspectClientsCsv(result.Value.rows, result.Value.filter);
                fileName = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateProspectClientsExcel(result.Value.rows, result.Value.filter);
                fileName = $"ProspectClients_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        var rowCount = result.Value.rows.Count;

        var subject = !string.IsNullOrWhiteSpace(emailSubject) ? emailSubject
            : $"Prospect Clients Report — {period} ({dbName})";

        var htmlBody = $@"
            <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                <h2 style='color:#1e293b'>Prospect Clients Report</h2>
                <p>Attached is the Prospect Clients report for <strong>{period}</strong>.</p>
                <table style='border-collapse:collapse;width:100%;margin:16px 0'>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Database</td><td style='padding:8px;border:1px solid #e2e8f0'>{dbName}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Period</td><td style='padding:8px;border:1px solid #e2e8f0'>{period}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Status</td><td style='padding:8px;border:1px solid #e2e8f0'>{result.Value.filter.StatusFilter}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Total Records</td><td style='padding:8px;border:1px solid #e2e8f0'>{rowCount:N0}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Sent By</td><td style='padding:8px;border:1px solid #e2e8f0'>{userName}</td></tr>
                </table>
                <p style='color:#64748b;font-size:12px'>Generated by Powersoft Reporting Engine</p>
            </div>";

        var attachments = new List<EmailAttachment>
        {
            new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType }
        };

        var ccJoined = ccList.valid.Length > 0 ? string.Join(",", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(",", bccList.valid) : null;

        int sentCount = 0;
        var errors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, "", attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Prospect Clients email to {Email}", email);
                errors.Add($"{email}: {ex.Message}");
            }
        }

        if (sentCount > 0)
        {
            _logger.LogInformation("Prospect Clients report emailed to {Count}/{Total} by {User}", sentCount, emails.Length, userName);
            var msg = $"Report sent to {sentCount} recipient(s)";
            if (errors.Count > 0) msg += $" ({errors.Count} failed)";
            return Json(new { success = true, message = msg });
        }

        return Json(new { success = false, message = $"Failed to send: {string.Join("; ", errors)}" });
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeProspectClientsReport(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "RegistrationDate",
        string statusFilter = "All", string priorityFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "RegistrationDate", string sortDirection = "DESC",
        bool includeHistory = false,
        string? locale = "en", int? promptTemplateId = null,
        string followedByFilter = "All", string category1Filter = "All", string category2Filter = "All",
        string customerCodesJson = "", bool customerExcludeMode = false,
        string statusCodesJson = "", string priorityCodesJson = "",
        string category1CodesJson = "", string category2CodesJson = "")
    {
        var result = await RunProspectClientsQuery(dateFrom, dateTo, dateField, statusFilter, priorityFilter,
            primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection, includeHistory,
            followedByFilter, category1Filter, category2Filter, customerCodesJson, customerExcludeMode,
            statusCodesJson, priorityCodesJson, category1CodesJson, category2CodesJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        try
        {
            var csvData = new CsvExportService().GenerateProspectClientsCsvString(result.Value.rows, result.Value.filter);
            var rowCount = result.Value.rows.Count;

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { }
                }
            }

            _logger.LogInformation(
                "AI analysis [ProspectClients]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                rowCount, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "ProspectClients", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Prospect Clients report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveProspectClientsSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        string? filterJson = null,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleProspectClients))
            return Json(new { success = false, message = "Not authorized to schedule this report." });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.ProspectClients);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

            var parsedTime = TimeSpan.TryParse(scheduleTime, out var ts) ? ts : new TimeSpan(8, 0, 0);
            DateTime? nextRun = null;
            if (string.Equals(recurrenceType, "Once", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(recurrenceJson))
                {
                    nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
                    if (nextRun == null)
                        return Json(new { success = false, message = "For 'Run once', please set a valid start date and time in the future." });
                }
                else nextRun = CalculateNextRun("Once", recurrenceDay, parsedTime);
            }
            else if (!string.IsNullOrWhiteSpace(recurrenceJson))
                nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);

            if (nextRun == null)
                nextRun = CalculateNextRun(recurrenceType ?? "Daily", recurrenceDay, parsedTime);

            // Accept parametersJson (shared partial) or legacy filterJson
            var paramsToStore = !string.IsNullOrWhiteSpace(parametersJson) ? parametersJson : (filterJson ?? "{}");

            var schedule = new ReportSchedule
            {
                ReportType     = ReportTypeConstants.ProspectClients,
                ScheduleName   = scheduleName,
                CreatedBy      = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay  = recurrenceDay,
                ScheduleTime   = parsedTime,
                ExportFormat   = exportFormat ?? "Excel",
                Recipients     = recipients,
                EmailSubject   = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(paramsToStore),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate    = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale       = aiLocale ?? "el",
                SkipIfEmpty    = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.ProspectClients);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving ProspectClients schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetProspectClientsSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.ProspectClients);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                s.SkipIfEmpty
            }));
        }
        catch { return Json(Array.Empty<object>()); }
    }

    // ===================== OFFERS REPORT =====================

    public async Task<IActionResult> OffersReport()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewOffersReport))
        {
            _logger.LogWarning("User {User} denied access to Offers Report (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewOffersReport);
            return RedirectToAction("AccessDenied", "Account");
        }

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderOffersReport,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout    = savedLayout;
        ViewBag.CanSchedule    = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleOffersReport);
        ViewBag.ViewCost       = CanViewCost();
        ViewBag.ViewSupplier   = CanViewSupplier();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetOffersReportLookups()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { stores = Array.Empty<object>(), agents = Array.Empty<object>(), statuses = Array.Empty<object>() });

        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
            await conn.OpenAsync();

            var stores = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DISTINCT st.pk_StoreCode, st.StoreName " +
                "FROM tbl_Store st INNER JOIN tbl_OfferHeader h ON h.fk_StoreCode = st.pk_StoreCode " +
                "ORDER BY st.StoreName", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    stores.Add(new { id = reader.GetString(0).Trim(), name = reader.GetString(1).Trim() });
            }

            var agents = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT DISTINCT a.pk_SystemNo, ISNULL(a.FirstName,'') + ' ' + ISNULL(a.LastName,'') AS AgentName " +
                "FROM tbl_Agent a INNER JOIN tbl_OfferHeader h ON h.fk_AgentID = a.pk_SystemNo " +
                "ORDER BY AgentName", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    agents.Add(new { id = reader.GetInt64(0), name = reader.GetString(1).Trim() });
            }

            var statuses = new List<object>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT pk_OrderStatusID, OrderStatusCode, OrderStatusName, OrderStatusHTML " +
                "FROM tbl_OrderStatus WHERE TableName = 'tbl_Offer' ORDER BY pk_OrderStatusID", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    statuses.Add(new
                    {
                        id = reader.GetInt64(0),
                        code = reader.GetString(1).Trim(),
                        name = reader.GetString(2).Trim(),
                        color = reader.IsDBNull(3) ? "#999999" : reader.GetString(3).Trim()
                    });
            }

            return Json(new { stores, agents, statuses });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OffersReport lookups");
            return Json(new { stores = Array.Empty<object>(), agents = Array.Empty<object>(), statuses = Array.Empty<object>() });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetOffersReportData(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All",
        string storeFilter = "All",
        string agentFilter = "All",
        string primaryGroup = "NONE",
        string secondaryGroup = "NONE",
        int maxRecords = 50000,
        string sortColumn = "DateTrans",
        string sortDirection = "DESC",
        string offerType = "All",
        bool includeHistory = false,
        string customerCodesJson = "",
        bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = new OffersReportFilter
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                DateField = dateField,
                StatusFilter = statusFilter,
                StoreFilter = storeFilter,
                AgentFilter = agentFilter,
                PrimaryGroup = primaryGroup ?? "NONE",
                SecondaryGroup = secondaryGroup ?? "NONE",
                ThirdGroup = thirdGroup ?? "NONE",
                MaxRecords = maxRecords,
                SortColumn = sortColumn,
                SortDirection = sortDirection,
                OfferType = offerType ?? "All",
                IncludeHistory = includeHistory,
                CustomerCodes = ParseCustomerCodesJson(customerCodesJson),
                CustomerExcludeMode = customerExcludeMode,
                StatusCodes = ParseCustomerCodesJson(statusCodesJson),
                StoreCodes = ParseCustomerCodesJson(storeCodesJson),
                AgentCodes = ParseCustomerCodesJson(agentCodesJson),
                ItemsSelectionJson = string.IsNullOrWhiteSpace(itemsSelectionJson) ? null : itemsSelectionJson
            };

            var repo = _repositoryFactory.CreateOffersReportRepository(tenantConnString);
            var (rows, totalRecords) = await repo.GetDataAsync(filter);
            return Json(new { success = true, data = rows, totalRows = rows.Count, totalRecords });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading offers report data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // --- Offers Report Layout CRUD ---

    [HttpPost]
    public async Task<IActionResult> SaveOffersReportLayout([FromBody] Dictionary<string, string> parameters)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (parameters == null || parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            await repo.SaveLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderOffersReport,
                ModuleConstants.IniDescriptionOffersReport,
                GetUserCode(),
                parameters);
            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OffersReport layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetOffersReportLayout()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderOffersReport,
                GetUserCode());
            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting OffersReport layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListOffersReportLayouts()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var layouts = await repo.ListLayoutsAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderOffersReport,
                GetUserCode());
            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode = l.HeaderCode, name = l.Name, isPublic = l.IsPublic,
                    createdBy = l.CreatedBy, canEdit = l.CanEdit, lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing OffersReport layouts for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveOffersReportLayoutAsRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveOffersReportLayoutAs([FromBody] SaveOffersReportLayoutAsRequest req)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderOffersReport,
                ModuleConstants.IniDescriptionOffersReport,
                GetUserCode(),
                req.Name, req.IsPublic, req.Parameters);
            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving named OffersReport layout for user {User}", GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadOffersReportLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            if (parms.Count == 0)
                return Json(new { success = false, message = "Layout not found or not visible" });
            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OffersReport layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteOffersReportLayout([FromQuery] string headerCode)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected" });
        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(tenantConnString);
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting OffersReport layout {Header} for user {User}", headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to delete layout" });
        }
    }

    // --- Offers Report Export / Email / AI ---

    private async Task<(List<OffersReportRow> rows, OffersReportFilter filter)?> RunOffersReportQuery(
        DateTime dateFrom, DateTime dateTo, string dateField, string statusFilter,
        string storeFilter, string agentFilter,
        string primaryGroup, string secondaryGroup, int maxRecords,
        string sortColumn, string sortDirection,
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new OffersReportFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            DateField = dateField,
            StatusFilter = statusFilter,
            StoreFilter = storeFilter,
            AgentFilter = agentFilter,
            PrimaryGroup = primaryGroup ?? "NONE",
            SecondaryGroup = secondaryGroup ?? "NONE",
            ThirdGroup = thirdGroup ?? "NONE",
            MaxRecords = maxRecords,
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            OfferType = offerType ?? "All",
            IncludeHistory = includeHistory,
            CustomerCodes = ParseCustomerCodesJson(customerCodesJson),
            CustomerExcludeMode = customerExcludeMode,
            StatusCodes = ParseCustomerCodesJson(statusCodesJson),
            StoreCodes = ParseCustomerCodesJson(storeCodesJson),
            AgentCodes = ParseCustomerCodesJson(agentCodesJson),
            ItemsSelectionJson = string.IsNullOrWhiteSpace(itemsSelectionJson) ? null : itemsSelectionJson
        };

        try
        {
            var repo = _repositoryFactory.CreateOffersReportRepository(tenantConnString);
            var (rows, _) = await repo.GetDataAsync(filter);
            return (rows, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running OffersReport query");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportOffersReportCsv(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null) return RedirectToAction("OffersReport");

        var bytes = new CsvExportService().GenerateOffersReportCsv(result.Value.rows, result.Value.filter, CanViewCost());
        return File(bytes, "text/csv", $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportOffersReportExcel(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null) return RedirectToAction("OffersReport");

        var bytes = new ExcelExportService().GenerateOffersReportExcel(result.Value.rows, result.Value.filter, CanViewCost());
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportOffersReportPdf(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null) return RedirectToAction("OffersReport");

        var bytes = new PdfExportService().GenerateOffersReportPdf(result.Value.rows, result.Value.filter, CanViewCost());
        return File(bytes, "application/pdf",
            $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> OffersReportPrintPreview(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null) return RedirectToAction("OffersReport");

        ViewBag.Rows = result.Value.rows;
        ViewBag.Filter = result.Value.filter;
        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
        ViewBag.ViewCost = CanViewCost();
        return View("OffersReportPrintPreview");
    }

    [HttpPost]
    public async Task<IActionResult> SendOffersReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat,
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Please enter at least one email address." });

        var emails = recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var invalidEmails = emails.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        if (invalidEmails.Length > 0)
            return Json(new { success = false, message = $"Invalid email: {string.Join(", ", invalidEmails)}" });

        var ccList = ParseAndValidateEmailList(cc);
        var bccList = ParseAndValidateEmailList(bcc);
        if (ccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid CC: {string.Join(", ", ccList.invalid)}" });
        if (bccList.invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid BCC: {string.Join(", ", bccList.invalid)}" });

        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateOffersReportPdf(result.Value.rows, result.Value.filter, CanViewCost());
                fileName = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateOffersReportCsv(result.Value.rows, result.Value.filter, CanViewCost());
                fileName = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateOffersReportExcel(result.Value.rows, result.Value.filter, CanViewCost());
                fileName = $"OffersReport_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        var rowCount = result.Value.rows.Count;

        var subject = !string.IsNullOrWhiteSpace(emailSubject) ? emailSubject
            : $"Offers Report \u2014 {period} ({dbName})";

        var htmlBody = $@"
            <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                <h2 style='color:#1e293b'>Offers Report</h2>
                <p>Attached is the Offers report for <strong>{period}</strong>.</p>
                <table style='border-collapse:collapse;width:100%;margin:16px 0'>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Database</td><td style='padding:8px;border:1px solid #e2e8f0'>{dbName}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Period</td><td style='padding:8px;border:1px solid #e2e8f0'>{period}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Status</td><td style='padding:8px;border:1px solid #e2e8f0'>{result.Value.filter.StatusFilter}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Total Records</td><td style='padding:8px;border:1px solid #e2e8f0'>{rowCount:N0}</td></tr>
                    <tr><td style='padding:8px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:600'>Sent By</td><td style='padding:8px;border:1px solid #e2e8f0'>{userName}</td></tr>
                </table>
                <p style='color:#64748b;font-size:12px'>Generated by Powersoft Reporting Engine</p>
            </div>";

        var attachments = new List<EmailAttachment>
        {
            new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType }
        };

        var ccJoined = ccList.valid.Length > 0 ? string.Join(",", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(",", bccList.valid) : null;

        int sentCount = 0;
        var errors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, "", attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Offers report email to {Email}", email);
                errors.Add($"{email}: {ex.Message}");
            }
        }

        if (sentCount > 0)
        {
            _logger.LogInformation("Offers report emailed to {Count}/{Total} by {User}", sentCount, emails.Length, userName);
            var msg = $"Report sent to {sentCount} recipient(s)";
            if (errors.Count > 0) msg += $" ({errors.Count} failed)";
            return Json(new { success = true, message = msg });
        }

        return Json(new { success = false, message = $"Failed to send: {string.Join("; ", errors)}" });
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeOffersReport(
        DateTime dateFrom, DateTime dateTo,
        string dateField = "DateTrans",
        string statusFilter = "All", string storeFilter = "All", string agentFilter = "All",
        string primaryGroup = "NONE", string secondaryGroup = "NONE",
        int maxRecords = 50000, string sortColumn = "DateTrans", string sortDirection = "DESC",
        string? locale = "en", int? promptTemplateId = null,
        string offerType = "All", bool includeHistory = false,
        string customerCodesJson = "", bool customerExcludeMode = false,
        string thirdGroup = "NONE",
        string statusCodesJson = "", string storeCodesJson = "", string agentCodesJson = "",
        string itemsSelectionJson = "")
    {
        var result = await RunOffersReportQuery(dateFrom, dateTo, dateField, statusFilter,
            storeFilter, agentFilter, primaryGroup, secondaryGroup, maxRecords, sortColumn, sortDirection,
            offerType, includeHistory, customerCodesJson, customerExcludeMode,
            thirdGroup, statusCodesJson, storeCodesJson, agentCodesJson, itemsSelectionJson);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        try
        {
            var csvData = new CsvExportService().GenerateOffersReportCsvString(result.Value.rows, result.Value.filter, CanViewCost());
            var rowCount = result.Value.rows.Count;

            string? customPrompt = null;
            if (promptTemplateId.HasValue && promptTemplateId.Value > 0)
            {
                var tenantConn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(tenantConn))
                {
                    try
                    {
                        var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConn);
                        var tpl = await schedRepo.GetAiPromptTemplateByIdAsync(promptTemplateId.Value);
                        if (tpl != null) customPrompt = tpl.SystemPrompt;
                    }
                    catch { }
                }
            }

            _logger.LogInformation(
                "AI analysis [OffersReport]: {Rows} data rows, {CsvLen} chars, locale={Locale}, user={User}",
                rowCount, csvData.Length, locale, User.Identity?.Name);

            var analyzer = _analyzerFactory.Create();
            var analysis = await analyzer.AnalyzeAsync(csvData, "OffersReport", locale: locale, customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Offers report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveOffersReportSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        string? filterJson = null,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleOffersReport))
            return Json(new { success = false, message = "Not authorized to schedule this report." });

        if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(recipients))
            return Json(new { success = false, message = "Schedule name and recipients are required" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);

            if (scheduleId <= 0)
            {
                var maxSchedules = await GetMaxSchedulesPerReportAsync(tenantConnString);
                var count = await repo.CountActiveSchedulesForReportAsync(ReportTypeConstants.OffersReport);
                if (count >= maxSchedules)
                    return Json(new { success = false, message = $"Schedule limit reached. Maximum {maxSchedules} active schedules per report." });
            }

            var parsedTime = TimeSpan.TryParse(scheduleTime, out var ts) ? ts : new TimeSpan(8, 0, 0);
            DateTime? nextRun = null;
            if (string.Equals(recurrenceType, "Once", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(recurrenceJson))
                {
                    nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);
                    if (nextRun == null)
                        return Json(new { success = false, message = "For 'Run once', please set a valid start date and time in the future." });
                }
                else nextRun = CalculateNextRun("Once", recurrenceDay, parsedTime);
            }
            else if (!string.IsNullOrWhiteSpace(recurrenceJson))
                nextRun = RecurrenceNextRunCalculator.GetNextRun(recurrenceJson, DateTime.Now);

            if (nextRun == null)
                nextRun = CalculateNextRun(recurrenceType ?? "Daily", recurrenceDay, parsedTime);

            var paramsToStore = !string.IsNullOrWhiteSpace(parametersJson) ? parametersJson : (filterJson ?? "{}");

            var schedule = new ReportSchedule
            {
                ReportType     = ReportTypeConstants.OffersReport,
                ScheduleName   = scheduleName,
                CreatedBy      = User.Identity?.Name ?? "Unknown",
                RecurrenceType = recurrenceType ?? "Daily",
                RecurrenceDay  = recurrenceDay,
                ScheduleTime   = parsedTime,
                ExportFormat   = exportFormat ?? "Excel",
                Recipients     = recipients,
                EmailSubject   = emailSubject,
                ParametersJson = InjectPermissionsIntoParametersJson(paramsToStore),
                RecurrenceJson = string.IsNullOrWhiteSpace(recurrenceJson) ? null : recurrenceJson,
                NextRunDate    = nextRun,
                IncludeAiAnalysis = includeAiAnalysis,
                AiLocale       = aiLocale ?? "el",
                SkipIfEmpty    = skipIfEmpty
            };

            if (scheduleId > 0)
            {
                var existing = await repo.GetScheduleByIdAsync(scheduleId);
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.OffersReport);
                if (!ok)
                    return Json(new { success = false, message });

                schedule.ScheduleId = scheduleId;
                schedule.IsActive = true;
                var updated = await repo.UpdateScheduleAsync(schedule);
                if (!updated)
                    return Json(new { success = false, message = "Failed to update schedule." });

                return Json(new { success = true, scheduleId, updated = true, message = "Schedule updated successfully" });
            }

            var id = await repo.CreateScheduleAsync(schedule);
            return Json(new { success = true, scheduleId = id, updated = false, message = "Schedule saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OffersReport schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOffersReportSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.OffersReport);
            return Json(schedules.Select(s => new
            {
                s.ScheduleId, s.ScheduleName, s.RecurrenceType, s.ExportFormat,
                scheduleTime = s.ScheduleTime.ToString(@"hh\:mm"),
                nextRun = s.NextRunDate?.ToString("yyyy-MM-dd HH:mm"),
                s.SkipIfEmpty
            }));
        }
        catch { return Json(Array.Empty<object>()); }
    }

    #region Filter Presets

    [HttpGet]
    public async Task<IActionResult> GetFilterPresets(string? reportType)
    {
        var connStr = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connStr))
            return Json(Array.Empty<object>());

        var userCode = HttpContext.Session.GetString(SessionKeys.UserCode) ?? "";
        var presets = await _filterPresetRepo.GetPresetsAsync(connStr, userCode, reportType);
        return Json(presets.Select(p => new
        {
            p.PresetId, p.PresetName, p.ReportType, p.FilterJson,
            p.IsShared, isOwner = p.CreatedBy == userCode
        }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveFilterPreset([FromBody] SaveFilterPresetRequest request)
    {
        var connStr = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connStr))
            return Json(new { success = false, message = "No database connected" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return Json(new { success = false, message = "Preset name is required" });

        var userCode = HttpContext.Session.GetString(SessionKeys.UserCode) ?? "";
        var preset = new FilterPreset
        {
            PresetId = request.PresetId,
            PresetName = request.Name.Trim(),
            ReportType = request.ReportType,
            FilterJson = request.FilterJson ?? "{}",
            CreatedBy = userCode,
            IsShared = request.IsShared
        };

        var id = await _filterPresetRepo.SavePresetAsync(connStr, preset);
        return Json(new { success = true, presetId = id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteFilterPreset(int presetId)
    {
        var connStr = GetTenantConnectionString();
        if (string.IsNullOrEmpty(connStr))
            return Json(new { success = false });

        var userCode = HttpContext.Session.GetString(SessionKeys.UserCode) ?? "";
        var ok = await _filterPresetRepo.DeletePresetAsync(connStr, presetId, userCode);
        return Json(new { success = ok });
    }

    public class SaveFilterPresetRequest
    {
        public int PresetId { get; set; }
        public string Name { get; set; } = "";
        public string? ReportType { get; set; }
        public string? FilterJson { get; set; }
        public bool IsShared { get; set; }
    }

    #endregion

}
