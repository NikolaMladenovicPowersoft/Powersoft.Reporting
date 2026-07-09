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
using Powersoft.Reporting.Web.Options;
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
    private readonly AiAnalyzerOptions _aiOptions;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ReportsController(
        ITenantRepositoryFactory repositoryFactory,
        ICentralRepository centralRepository,
        IEmailSender emailSender,
        ReportAnalyzerFactory analyzerFactory,
        IFilterPresetRepository filterPresetRepo,
        ILogger<ReportsController> logger,
        Microsoft.Extensions.Options.IOptions<Options.AiAnalyzerOptions> aiOptions)
    {
        _repositoryFactory = repositoryFactory;
        _centralRepository = centralRepository;
        _emailSender = emailSender;
        _analyzerFactory = analyzerFactory;
        _filterPresetRepo = filterPresetRepo;
        _logger = logger;
        _aiOptions = aiOptions.Value;
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

        // First check the specific RENGINEAI action
        if (await _centralRepository.IsActionAuthorizedAsync(roleId, actionId))
            return true;

        // Fallback: legacy "View PowerReports" (5100) grants access to all reports
        return await _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewPowerReportsLegacy);
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
            ViewBag.CanViewTrialBalance = true;
            ViewBag.CanViewProfitLoss = true;
            ViewBag.CanViewCashFlow   = true;
        }
        else
        {
            var roleId = GetRoleID();
            // Run all checks concurrently — single await at end
            var t1  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewAvgBasket);
            var t2  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewPurchasesSales);
            var t3  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewPareto);
            var t4  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCharts);
            var t5  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCatalogue);
            var t6  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewBelowMinStock);
            var t7  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCancelLog);
            var t8  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewProspectClients);
            var t9  = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewOffersReport);
            var t10 = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewTrialBalance);
            var t11 = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewProfitLoss);
            var t12 = _centralRepository.IsActionAuthorizedAsync(roleId, ModuleConstants.ActionViewCashFlow);
            await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12);

            ViewBag.CanViewAB         = t1.Result;
            ViewBag.CanViewPS         = t2.Result && CanViewCost();
            ViewBag.CanViewPareto     = t3.Result;
            ViewBag.CanViewCharts     = t4.Result;
            ViewBag.CanViewCatalogue  = t5.Result;
            ViewBag.CanViewBMS        = t6.Result;
            ViewBag.CanViewCancelLog  = t7.Result;
            ViewBag.CanViewPC         = t8.Result;
            ViewBag.CanViewOffers     = t9.Result;
            ViewBag.CanViewTrialBalance = t10.Result;
            ViewBag.CanViewProfitLoss = t11.Result;
            ViewBag.CanViewCashFlow   = t12.Result;
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

    /// <summary>
    /// Logs AI token usage after a successful analysis call.
    /// </summary>
    private async Task LogAiTokensAsync(string tenantConnString, int inputTokens, int outputTokens)
    {
        try
        {
            var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            await schedRepo.IncrementTokenUsageAsync(inputTokens, outputTokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log AI token usage ({In}+{Out})", inputTokens, outputTokens);
        }
    }

    private const int MaxCsvBytesForAi = 100_000;

    /// <summary>Outcome of the pre-run AI cost/budget guard.</summary>
    private sealed class AiGuardResult
    {
        public ReportAnalysis? Analysis { get; init; }
        public string? Error { get; init; }
        public bool NeedsConfirmation { get; init; }
        public decimal EstimatedCost { get; init; }
    }

    /// <summary>
    /// Converts a guard outcome into an early JSON response, or null when the analysis may proceed.
    /// Hard cap / monthly budget → blocking error; soft threshold → needsConfirmation prompt.
    /// </summary>
    private IActionResult? AiGuardFailure(AiGuardResult g)
    {
        if (g.Error != null)
            return Json(new { success = false, message = g.Error });
        if (g.NeedsConfirmation)
            return Json(new
            {
                success = false,
                needsConfirmation = true,
                estimatedCost = g.EstimatedCost,
                message = $"This analysis is estimated to cost about ${g.EstimatedCost:0.000} (USD). Do you want to proceed?"
            });
        return null;
    }

    /// <summary>
    /// Pre-run cost guard + AI call + token logging in one step. The cost is estimated on the
    /// FULL data (worst case) BEFORE any tokens are spent, so a huge report is blocked (hard cap)
    /// or requires confirmation (soft threshold) instead of silently burning the budget.
    /// The client may resend with form field confirmCost=true to bypass the soft threshold.
    /// </summary>
    private async Task<AiGuardResult> AnalyzeWithBudgetAsync(
        string csvData, string reportType, string? locale, string? customPrompt, string? tenantConnString)
    {
        var estimatedCost = AiCostEstimator.EstimateCost(csvData, _aiOptions);
        var confirmed = string.Equals(Request.Form["confirmCost"], "true", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(tenantConnString))
        {
            decimal softLimit = 0.10m, hardLimit = 0.25m;
            try
            {
                var schedRepo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
                var budget = await schedRepo.GetOrCreateTokenBudgetAsync();

                // Monthly cumulative token budget — existing hard block.
                if (budget.MonthlyTokenLimit > 0 && budget.CurrentMonthUsed >= budget.MonthlyTokenLimit)
                    return new AiGuardResult
                    {
                        Error = $"Monthly AI token budget exceeded ({budget.CurrentMonthUsed:N0} / {budget.MonthlyTokenLimit:N0} tokens used this month)."
                    };

                softLimit = budget.SoftCostLimit;
                hardLimit = budget.HardCostLimit;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load AI budget/limits — using defaults");
            }

            // Hard cap: block outright, no tokens spent.
            if (hardLimit > 0 && estimatedCost > hardLimit)
                return new AiGuardResult
                {
                    Error = $"This report is too large for AI analysis (estimated ${estimatedCost:0.000}, limit ${hardLimit:0.00}). Narrow the date range or filters and try again."
                };

            // Soft threshold: require explicit confirmation.
            if (softLimit > 0 && estimatedCost > softLimit && !confirmed)
                return new AiGuardResult { NeedsConfirmation = true, EstimatedCost = estimatedCost };
        }

        if (Encoding.UTF8.GetByteCount(csvData) > MaxCsvBytesForAi)
        {
            var bytes = Encoding.UTF8.GetBytes(csvData);
            csvData = Encoding.UTF8.GetString(bytes, 0, MaxCsvBytesForAi);
            var lastNewline = csvData.LastIndexOf('\n');
            if (lastNewline > 0) csvData = csvData[..lastNewline];
            _logger.LogInformation("AI CSV truncated from {Original} to {Truncated} bytes for {Report}",
                bytes.Length, Encoding.UTF8.GetByteCount(csvData), reportType);
        }

        var analyzer = _analyzerFactory.Create();
        var analysis = await analyzer.AnalyzeAsync(csvData, reportType, locale: locale,
            customSystemPrompt: customPrompt, ct: HttpContext.RequestAborted);

        if (!string.IsNullOrEmpty(tenantConnString))
            await LogAiTokensAsync(tenantConnString, analysis.InputTokens, analysis.OutputTokens);

        await LogAiUsageCentralAsync(reportType, analysis, estimatedCost);

        return new AiGuardResult { Analysis = analysis, EstimatedCost = estimatedCost };
    }

    /// <summary>
    /// Records the analysis centrally (psCentral) so a Powersoft admin can see cross-tenant
    /// usage in one report. Best-effort: never throws into the analysis flow.
    /// </summary>
    private async Task LogAiUsageCentralAsync(string reportType, ReportAnalysis analysis, decimal estimatedCost)
    {
        try
        {
            await _centralRepository.LogAiUsageAsync(new Core.Models.AiUsageLogEntry
            {
                DBCode = HttpContext.Session.GetString(SessionKeys.ConnectedDatabaseCode) ?? "",
                DBName = HttpContext.Session.GetString(SessionKeys.ConnectedDatabase),
                UserCode = GetUserCode(),
                ReportType = reportType,
                InputTokens = analysis.InputTokens,
                OutputTokens = analysis.OutputTokens,
                EstimatedCost = estimatedCost,
                ActualCost = AiCostEstimator.ComputeCost(analysis.InputTokens, analysis.OutputTokens, _aiOptions),
                Source = "Interactive"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log central AI usage for {Report}", reportType);
        }
    }

    // ==================== Generic Layout Endpoints (shared across all reports) ====================

    /// <summary>
    /// Maps a ReportTypeConstants value to the INI header code + description used by IniRepository.
    /// Returns null when the report type is unknown (caller should return 400/404).
    /// </summary>
    private static (string Header, string Description)? ResolveLayoutSlug(string reportType) =>
        reportType switch
        {
            ReportTypeConstants.AverageBasket  => (ModuleConstants.IniHeaderAvgBasket,        ModuleConstants.IniDescriptionAvgBasket),
            ReportTypeConstants.PurchasesSales => (ModuleConstants.IniHeaderPurchasesSales,   ModuleConstants.IniDescriptionPurchasesSales),
            ReportTypeConstants.Catalogue      => (ModuleConstants.IniHeaderCatalogue,        ModuleConstants.IniDescriptionCatalogue),
            ReportTypeConstants.Pareto         => (ModuleConstants.IniHeaderPareto,           ModuleConstants.IniDescriptionPareto),
            ReportTypeConstants.Charts         => (ModuleConstants.IniHeaderCharts,           ModuleConstants.IniDescriptionCharts),
            ReportTypeConstants.CancelLog      => (ModuleConstants.IniHeaderCancelLog,        ModuleConstants.IniDescriptionCancelLog),
            ReportTypeConstants.ProspectClients=> (ModuleConstants.IniHeaderProspectClients,  ModuleConstants.IniDescriptionProspectClients),
            ReportTypeConstants.OffersReport   => (ModuleConstants.IniHeaderOffersReport,     ModuleConstants.IniDescriptionOffersReport),
            ReportTypeConstants.BelowMinStock  => (ModuleConstants.IniHeaderBelowMinStock,    ModuleConstants.IniDescriptionBelowMinStock),
            ReportTypeConstants.TrialBalance   => (ModuleConstants.IniHeaderTrialBalance,     ModuleConstants.IniDescriptionTrialBalance),
            ReportTypeConstants.ProfitLoss     => (ModuleConstants.IniHeaderProfitLoss,       ModuleConstants.IniDescriptionProfitLoss),
            ReportTypeConstants.CustomerNotPurchased => (ModuleConstants.IniHeaderCustomerNotPurchased, ModuleConstants.IniDescriptionCustomerNotPurchased),
            ReportTypeConstants.CashFlow       => (ModuleConstants.IniHeaderCashFlow,         ModuleConstants.IniDescriptionCashFlow),
            _                                  => null
        };

    [HttpGet]
    public async Task<IActionResult> GetReportLayout([FromQuery] string reportType)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var parms = await repo.GetLayoutAsync(ModuleConstants.ModuleCode, slug.Value.Header, GetUserCode());
            return Json(new { success = true, hasSaved = parms.Count > 0, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetReportLayout error for {Report}/{User}", reportType, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveReportLayout([FromQuery] string reportType,
                                                       [FromBody] Dictionary<string, string> parameters)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        if (parameters == null || parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            await repo.SaveLayoutAsync(ModuleConstants.ModuleCode, slug.Value.Header,
                slug.Value.Description, GetUserCode(), parameters);
            return Json(new { success = true, message = "Layout saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveReportLayout error for {Report}/{User}", reportType, GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetReportLayout([FromQuery] string reportType)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var deleted = await repo.DeleteLayoutAsync(ModuleConstants.ModuleCode, slug.Value.Header, GetUserCode());
            return Json(new { success = true, message = deleted ? "Layout reset to defaults" : "No saved layout found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetReportLayout error for {Report}/{User}", reportType, GetUserCode());
            return Json(new { success = false, message = "Failed to reset layout" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListReportLayouts([FromQuery] string reportType)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type", layouts = Array.Empty<object>() });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected", layouts = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var layouts = await repo.ListLayoutsAsync(ModuleConstants.ModuleCode, slug.Value.Header, GetUserCode());
            return Json(new
            {
                success = true,
                layouts = layouts.Select(l => new
                {
                    headerCode   = l.HeaderCode,
                    name         = l.Name,
                    isPublic     = l.IsPublic,
                    createdBy    = l.CreatedBy,
                    canEdit      = l.CanEdit,
                    lastModified = l.LastModified
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListReportLayouts error for {Report}/{User}", reportType, GetUserCode());
            return Json(new { success = false, message = "Failed to list layouts", layouts = Array.Empty<object>() });
        }
    }

    public class SaveReportLayoutAsRequest
    {
        public string Name       { get; set; } = string.Empty;
        public bool   IsPublic   { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> SaveReportLayoutAs([FromQuery] string reportType,
                                                         [FromBody] SaveReportLayoutAsRequest req)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        if (req == null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Layout name is required" });
        if (req.Parameters == null || req.Parameters.Count == 0)
            return Json(new { success = false, message = "No parameters to save" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var headerCode = await repo.SaveNamedLayoutAsync(
                ModuleConstants.ModuleCode, slug.Value.Header, slug.Value.Description,
                GetUserCode(), req.Name, req.IsPublic, req.Parameters);
            return Json(new { success = true, headerCode, message = "Layout saved" });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveReportLayoutAs error for {Report}/{User}", reportType, GetUserCode());
            return Json(new { success = false, message = "Failed to save layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> LoadReportLayout([FromQuery] string reportType,
                                                       [FromQuery] string headerCode)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var parms = await repo.GetNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            if (parms.Count == 0) return Json(new { success = false, message = "Layout not found or not visible" });
            return Json(new { success = true, parameters = parms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadReportLayout error for {Report}/{Header}/{User}", reportType, headerCode, GetUserCode());
            return Json(new { success = false, message = "Failed to load layout" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteReportLayout([FromQuery] string reportType,
                                                         [FromQuery] string headerCode)
    {
        var slug = ResolveLayoutSlug(reportType);
        if (slug == null) return Json(new { success = false, message = "Unknown report type" });

        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return Json(new { success = false, message = "Not connected" });

        if (string.IsNullOrWhiteSpace(headerCode))
            return Json(new { success = false, message = "headerCode is required" });

        try
        {
            var repo = _repositoryFactory.CreateIniRepository(conn);
            var deleted = await repo.DeleteNamedLayoutAsync(ModuleConstants.ModuleCode, headerCode, GetUserCode());
            return Json(new
            {
                success = deleted,
                message = deleted ? "Layout deleted" : "Layout not found or you don't have permission"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteReportLayout error for {Report}/{Header}/{User}", reportType, headerCode, GetUserCode());
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
    public IActionResult AllSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }
        ViewBag.ConnectedDatabase = HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);
        ViewBag.IsWebmaster = GetRanking() == ModuleConstants.RankingWebmaster;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAllSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new List<object>());
        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var all = await repo.GetAllSchedulesAsync();
            return Json(all.Select(s => new
            {
                id = s.Id, reportType = s.ReportType, name = s.Name,
                createdBy = s.CreatedBy, createdDate = s.CreatedDate.ToString("dd/MM/yyyy"),
                isActive = s.IsActive, recurrenceType = s.RecurrenceType,
                nextRun = s.NextRun?.ToString("dd/MM/yyyy HH:mm"),
                lastRun = s.LastRun?.ToString("dd/MM/yyyy HH:mm"),
                exportFormat = s.ExportFormat, recipients = s.Recipients,
                starRating = s.StarRating
            }));
        }
        catch { return Json(new List<object>()); }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateScheduleRating(int scheduleId, byte? rating)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false });
        try
        {
            if (rating.HasValue && (rating < 1 || rating > 5)) rating = null;
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            await repo.UpdateStarRatingAsync(scheduleId, rating);
            return Json(new { success = true });
        }
        catch { return Json(new { success = false }); }
    }

    [HttpGet]
    public async Task<IActionResult> GetDatabaseUsers()
    {
        var dbCode = HttpContext.Session.GetString(SessionKeys.ConnectedDatabaseCode);
        if (string.IsNullOrEmpty(dbCode))
            return Json(new List<object>());
        try
        {
            var users = await _centralRepository.GetUsersForDatabaseAsync(dbCode);
            return Json(users.Select(u => new { code = u.UserCode, name = u.DisplayName, email = u.Email }));
        }
        catch { return Json(new List<object>()); }
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
                l.ErrorMessage, l.DurationMs,
                l.InputTokens, l.OutputTokens, l.EstimatedCost
            }));
        }
        catch
        {
            return Json(new List<object>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTokenBudget()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var budget = await repo.GetCurrentTokenBudgetAsync();
            if (budget == null) return Json(new { });
            return Json(new
            {
                budget.MonthlyTokenLimit,
                budget.CurrentMonthUsed,
                budgetMonth = budget.BudgetMonth.ToString("yyyy-MM-dd")
            });
        }
        catch
        {
            return Json(new { });
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Average Basket", dbName, period, result.Value.rows.Count, exportFormat, userName, "Rows", selectionsHtml);

        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Average Basket Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Average Basket", dbName, period, result.Value.rows.Count, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "AverageBasket", templateId,
            fileBytes, fileName, contentType,
            $"Average Basket Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
    }

    /// <summary>
    /// Builds the standard merge-field dictionary shared by every report email. Keys match the
    /// <c>\u00AB..\u00BB</c> placeholders documented in the email-template editor.
    /// </summary>
    private static Dictionary<string, string> BuildEmailTokens(
        string reportName, string dbName, string period, int rowCount, string? exportFormat, string userName)
        => new()
        {
            ["ReportName"] = reportName,
            ["DatabaseName"] = dbName,
            ["CompanyName"] = dbName,
            ["Period"] = period,
            ["RowCount"] = rowCount.ToString(),
            ["ExportFormat"] = exportFormat ?? "Excel",
            ["GeneratedDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["UserName"] = userName,
        };

    /// <summary>
    /// Builds the default HTML email body — mirrors <c>ScheduleExecutionService.BuildEmailHtml</c>
    /// so manual and scheduled emails look identical.
    /// </summary>
    private static string BuildDefaultEmailHtmlBody(
        string reportName, string dbName, string period,
        int rowCount, string exportFormat, string userName,
        string rowCountLabel = "Rows",
        string? selectionsHtml = null)
    {
        var selSection = !string.IsNullOrEmpty(selectionsHtml)
            ? $@"
    <h4 style='margin:16px 0 8px;color:#374151;font-size:14px;font-weight:600;'>Selections for this report:</h4>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 16px;font-size:14px;'>{selectionsHtml}</table>"
            : "";
        return $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto;'>
  <table width='100%' cellpadding='0' cellspacing='0' border='0'><tr>
    <td style='background-color:#1e40af;padding:24px 32px;'>
      <h1 style='margin:0;color:#ffffff;font-size:20px;font-weight:600;'>Powersoft Reports</h1>
    </td>
  </tr></table>
  <div style='background-color:#ffffff;padding:28px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;'>
    <h2 style='margin:0 0 8px;color:#1e40af;font-size:18px;font-weight:700;'>{reportName}</h2>
    <p style='margin:0 0 20px;color:#374151;font-size:14px;'>{dbName}</p>
    <p style='margin:0 0 20px;color:#374151;font-size:14px;'>Please find the attached <strong>{reportName}</strong> report.</p>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='margin:0 0 20px;font-size:14px;'>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;width:120px;'>Period</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{period}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>{rowCountLabel}</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{rowCount}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#6b7280;'>Format</td>
        <td style='padding:10px 14px;border-bottom:1px solid #e5e7eb;color:#111827;'>{exportFormat}</td>
      </tr>
      <tr>
        <td style='padding:10px 14px;color:#6b7280;'>Generated</td>
        <td style='padding:10px 14px;color:#111827;'>{DateTime.Now:yyyy-MM-dd HH:mm}</td>
      </tr>
    </table>{selSection}
  </div>
  <table width='100%' cellpadding='0' cellspacing='0' border='0'><tr>
    <td style='background-color:#f3f4f6;padding:16px 32px;border-left:1px solid #d1d5db;border-right:1px solid #d1d5db;border-bottom:1px solid #d1d5db;'>
      <p style='margin:0;color:#6b7280;font-size:11px;'>
        Automated report by Powersoft Report Engine &bull; {dbName}
      </p>
    </td>
  </tr></table>
</div>";
    }

    /// <summary>
    /// Shared "send report email" tail used by every <c>SendXxxReportEmail</c> action. Owns everything
    /// that is identical across reports: recipient/CC/BCC validation, template resolution (by id; falls
    /// back to the supplied built-in default body when no template is chosen), merge-field substitution,
    /// attachment building and the per-recipient send loop. Per-report actions still own the
    /// "collect params -> run query -> produce export bytes -> build a default body" step and pass the
    /// results in here.
    /// </summary>
    private async Task<IActionResult> SendReportEmailCore(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string reportType, int? templateId,
        byte[] fileBytes, string fileName, string contentType,
        string defaultSubject, string defaultHtmlBody, string defaultTextBody,
        IDictionary<string, string> tokens)
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

        // Resolve the chosen template by id only. Selecting "-- No template (default) --" must honor the
        // built-in default body, so we never silently substitute the DB default here.
        string? templateBody = null;
        string? templateSubject = null;
        if (templateId.HasValue && templateId.Value > 0)
        {
            try
            {
                var conn = GetTenantConnectionString();
                if (!string.IsNullOrEmpty(conn))
                {
                    var schedRepo = _repositoryFactory.CreateScheduleRepository(conn);
                    var tmpl = await schedRepo.GetEmailTemplateByIdAsync(templateId.Value);
                    if (tmpl != null) { templateBody = tmpl.EmailBodyHtml; templateSubject = tmpl.EmailSubject; }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not load email template {Id} — falling back to default body", templateId); }
        }

        string Merge(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            foreach (var kv in tokens)
                text = text!.Replace("\u00AB" + kv.Key + "\u00BB", kv.Value ?? "");
            return text!.Replace("\u00AB", "").Replace("\u00BB", "");
        }

        const string emailBrand = "Powersoft 365 AI reports";
        var rawSubject = !string.IsNullOrWhiteSpace(emailSubject)
            ? Merge(emailSubject)
            : (templateSubject != null ? Merge(templateSubject) : Merge(defaultSubject));
        // Brand all outgoing report emails (George, 2026-06-08). Avoid double-prefixing.
        var subject = rawSubject.Contains(emailBrand, StringComparison.OrdinalIgnoreCase)
            ? rawSubject
            : $"{emailBrand} - {rawSubject}";

        var htmlBody = !string.IsNullOrWhiteSpace(templateBody) ? Merge(templateBody) : defaultHtmlBody;
        var textBody = defaultTextBody ?? "";

        var attachments = new[] { new EmailAttachment { FileName = fileName, Content = fileBytes, ContentType = contentType } };
        var ccJoined = ccList.valid.Length > 0 ? string.Join(";", ccList.valid) : null;
        var bccJoined = bccList.valid.Length > 0 ? string.Join(";", bccList.valid) : null;

        var sentCount = 0;
        var errors = new List<string>();
        foreach (var email in emails)
        {
            try
            {
                await _emailSender.SendAsync(email, ccJoined, bccJoined, subject, htmlBody, textBody, attachments);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send {ReportType} email to {Email}", reportType, email);
                errors.Add(email);
            }
        }

        if (errors.Count > 0 && sentCount == 0)
            return Json(new { success = false, message = $"Failed to send to: {string.Join(", ", errors)}" });

        var msg = sentCount == 1 ? $"Report sent to {emails[0]}" : $"Report sent to {sentCount} recipient(s)";
        if (errors.Count > 0) msg += $" (failed: {string.Join(", ", errors)})";
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Purchases vs Sales", dbName, period, result.Value.rows.Count, exportFormat, userName, "Rows", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Purchases vs Sales Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Purchases vs Sales", dbName, period, result.Value.rows.Count, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "PurchasesSales", templateId,
            fileBytes, fileName, contentType,
            $"Purchases vs Sales Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

            var tenantConn4AB = GetTenantConnectionString();
            var guardAB = await AnalyzeWithBudgetAsync(csvData, "AverageBasket", locale, customPrompt, tenantConn4AB);
            var guardFailAB = AiGuardFailure(guardAB);
            if (guardFailAB != null) return guardFailAB;
            var analysis = guardAB.Analysis;

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

            var guardPS = await AnalyzeWithBudgetAsync(csvData, "PurchasesSales", locale, customPrompt, GetTenantConnectionString());
            var guardFailPS = AiGuardFailure(guardPS);
            if (guardFailPS != null) return guardFailPS;
            var analysis = guardPS.Analysis;

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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Pareto 80/20", dbName, period, result.Rows.Count, exportFormat, userName, "Items", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Pareto 80/20 Report\nDatabase: {dbName}\nPeriod: {period}\nItems: {result.Rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Pareto 80/20", dbName, period, result.Rows.Count, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "Pareto", templateId,
            fileBytes, fileName, contentType,
            $"Pareto 80/20 Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

            var guardPareto = await AnalyzeWithBudgetAsync(csvData, "Pareto", locale, customPrompt, GetTenantConnectionString());
            var guardFailPareto = AiGuardFailure(guardPareto);
            if (guardFailPareto != null) return guardFailPareto;
            var analysis = guardPareto.Analysis;

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Pareto report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
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

            var guardChart = await AnalyzeWithBudgetAsync(csvData, reportContext, locale, customPrompt, GetTenantConnectionString());
            var guardFailChart = AiGuardFailure(guardChart);
            if (guardFailChart != null) return guardFailChart;
            var analysis = guardChart.Analysis;

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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Charts & Dashboards", dbName, period, data.Count, exportFormat, userName, "Data Points", selectionsHtml);

        var defaultTextBody = $"Charts & Dashboards Report\nDatabase: {dbName}\nPeriod: {period}\nData Points: {data.Count}\nFormat: {exportFormat}\n\nParameters:\n{string.Join("\n", selectionLines)}";

        var tokens = BuildEmailTokens("Charts & Dashboards", dbName, period, data.Count, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "Charts", templateId,
            fileBytes, fileName, contentType,
            $"Charts & Dashboards \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Power Reports Catalogue", dbName, period, result.Value.rows.Count, exportFormat, userName, "Rows", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Power Reports Catalogue\nDatabase: {dbName}\nPeriod: {period}\nRows: {result.Value.rows.Count}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Power Reports Catalogue", dbName, period, result.Value.rows.Count, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "Catalogue", templateId,
            fileBytes, fileName, contentType,
            $"Power Reports Catalogue \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

            var guardCat = await AnalyzeWithBudgetAsync(csvData, "Catalogue", locale, customPrompt, GetTenantConnectionString());
            var guardFailCat = AiGuardFailure(guardCat);
            if (guardFailCat != null) return guardFailCat;
            var analysis = guardCat.Analysis;

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

    private async Task<List<BelowMinStockRow>?> RunBmsExportQuery(
        string? storeCodes, string? itemsSelection,
        string sortColumn, string sortDirection)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = new BelowMinStockFilter
        {
            StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? null
                : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ItemsSelection = ParseItemsSelection(itemsSelection),
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

        var repo = _repositoryFactory.CreateBelowMinStockRepository(tenantConnString);
        return await repo.GetBelowMinStockAsync(filter);
    }

    public async Task<IActionResult> ExportBmsCsv(
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC")
    {
        var data = await RunBmsExportQuery(storeCodes, itemsSelection, sortColumn, sortDirection);
        if (data == null) return RedirectToAction("BelowMinStock");

        var service = new CsvExportService();
        var bytes = service.GenerateBelowMinStockCsv(data, new BelowMinStockFilter(), CanViewCost());
        return File(bytes, "text/csv", $"BelowMinStock_{DateTime.Now:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> ExportBmsExcel(
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC")
    {
        var data = await RunBmsExportQuery(storeCodes, itemsSelection, sortColumn, sortDirection);
        if (data == null) return RedirectToAction("BelowMinStock");

        var service = new ExcelExportService();
        var bytes = service.GenerateBelowMinStockExcel(data, new BelowMinStockFilter(), CanViewCost());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BelowMinStock_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> ExportBmsPdf(
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC")
    {
        var data = await RunBmsExportQuery(storeCodes, itemsSelection, sortColumn, sortDirection);
        if (data == null) return RedirectToAction("BelowMinStock");

        var service = new PdfExportService();
        var bytes = service.GenerateBelowMinStockPdf(data, new BelowMinStockFilter(), CanViewCost());
        return File(bytes, "application/pdf", $"BelowMinStock_{DateTime.Now:yyyyMMdd}.pdf");
    }

    [HttpPost]
    public async Task<IActionResult> SendBelowMinStockEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "ItemCode", string sortDirection = "ASC")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        List<BelowMinStockRow> data;
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
            data = await repo.GetBelowMinStockAsync(filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating below-minimum stock data for email");
            return Json(new { success = false, message = "Failed to generate report data." });
        }

        // Below Min Stock has no Excel/PDF exporter — the report is delivered as CSV (mirrors the scheduler).
        var viewCost = CanViewCost();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ItemCode,ItemName,Store,StoreName,Category,Department,Brand,CurrentStock,MinimumStock,Difference," + (viewCost ? "Cost,StockValue," : "") + "Shelf");
        foreach (var r in data)
        {
            sb.AppendLine($"\"{r.ItemCode}\",\"{r.ItemName}\",\"{r.StoreCode}\",\"{r.StoreName}\"," +
                $"\"{r.CategoryName}\",\"{r.DepartmentName}\",\"{r.BrandName}\"," +
                $"{r.CurrentStock},{r.MinimumStock},{r.Difference}," + (viewCost ? $"{r.Cost ?? 0},{r.StockValue ?? 0}," : "") + $"\"{r.Shelf}\"");
        }
        var fileBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"BelowMinStock_{DateTime.Now:yyyyMMdd}.csv";

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"As of {DateTime.Now:yyyy-MM-dd}";

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Below Min Stock", dbName, period, data.Count, "CSV", userName, "Items below minimum");
        var defaultTextBody = $"Below Minimum Stock Report\nDatabase: {dbName}\n{period}\nItems below minimum: {data.Count}";

        var tokens = BuildEmailTokens("Below Min Stock", dbName, period, data.Count, "CSV", userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "BelowMinStock", templateId,
            fileBytes, fileName, "text/csv",
            $"Below Minimum Stock Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

    // ==================== Items Not Purchased (by Customer) ====================

    public async Task<IActionResult> CustomerNotPurchased()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCustomerNotPurchased))
        {
            _logger.LogWarning("User {User} denied access to CustomerNotPurchased (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewCustomerNotPurchased);
            return RedirectToAction("AccessDenied", "Account");
        }

        var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
        var stores = await storeRepo.GetActiveStoresAsync();
        ViewBag.StoresJson = System.Text.Json.JsonSerializer.Serialize(
            stores.Select(s => new { code = s.StoreCode, name = s.StoreName }));
        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();
        ViewBag.CanSchedule  = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCustomerNotPurchased);
        ViewBag.ViewCost     = CanViewCost();
        ViewBag.ViewSupplier = CanViewSupplier();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetCustomerNotPurchasedData(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelection = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC",
        int pageNumber = 1, int pageSize = 100)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            // Only Item and Customer grouping are supported (see filter validation).
            if (!Enum.TryParse<GroupByType>(groupBy, true, out var gb) || gb != GroupByType.Customer)
                gb = GroupByType.Item;

            var filter = new CustomerNotPurchasedFilter
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                ReferenceDate = referenceDate ?? DateTime.Today,
                DaysThreshold = days,
                GroupBy = gb,
                IncludeNeverPurchased = includeNeverPurchased,
                CustomerCodes = string.IsNullOrWhiteSpace(customerCodes) ? new List<string>()
                    : customerCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                CustomerExcludeMode = customerExcludeMode,
                StoreCodes = string.IsNullOrWhiteSpace(storeCodes) ? new List<string>()
                    : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ItemsSelection = ParseItemsSelection(itemsSelection),
                SortColumn = sortColumn,
                SortDirection = sortDirection,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            if (!filter.IsValid(out var errors))
                return Json(new { success = false, message = string.Join(" ", errors) });

            var repo = _repositoryFactory.CreateCustomerNotPurchasedRepository(tenantConnString);
            var result = await repo.GetDataAsync(filter);

            return Json(new
            {
                success = true,
                data = result.Items,
                totalRows = result.TotalCount,
                pageNumber = result.PageNumber,
                pageSize = result.PageSize,
                totalPages = result.TotalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Items Not Purchased data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Builds a CNP filter from the same parameter surface used across export/email/AI/print/schedule.
    // allRows=true lifts the page cap so exports/analysis see the whole result set (bounded to 100k rows).
    private static CustomerNotPurchasedFilter BuildCnpFilterCore(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate,
        int days, string groupBy, bool includeNeverPurchased,
        string? customerCodes, bool customerExcludeMode,
        List<string> storeList, ItemsSelectionFilter? itemsSelection,
        string sortColumn, string sortDirection, bool allRows)
    {
        if (!Enum.TryParse<GroupByType>(groupBy, true, out var gb) || gb != GroupByType.Customer)
            gb = GroupByType.Item;

        return new CustomerNotPurchasedFilter
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReferenceDate = referenceDate ?? DateTime.Today,
            DaysThreshold = days,
            GroupBy = gb,
            IncludeNeverPurchased = includeNeverPurchased,
            CustomerCodes = string.IsNullOrWhiteSpace(customerCodes) ? new List<string>()
                : customerCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            CustomerExcludeMode = customerExcludeMode,
            StoreCodes = storeList,
            ItemsSelection = itemsSelection,
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            PageNumber = 1,
            PageSize = allRows ? 100000 : 100,
            MaxRecords = allRows ? 100000 : 10000
        };
    }

    private async Task<(List<CustomerNotPurchasedRow> rows, CustomerNotPurchasedFilter filter)?> RunCnpExportQuery(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate,
        int days, string groupBy, bool includeNeverPurchased,
        string? customerCodes, bool customerExcludeMode,
        string? storeCodes, string? itemsSelection,
        string sortColumn, string sortDirection)
    {
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn)) return null;

        var storeList = string.IsNullOrWhiteSpace(storeCodes) ? new List<string>()
            : storeCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var filter = BuildCnpFilterCore(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeList, ParseItemsSelection(itemsSelection),
            sortColumn, sortDirection, allRows: true);

        if (!filter.IsValid(out _)) return null;

        var repo = _repositoryFactory.CreateCustomerNotPurchasedRepository(conn);
        var result = await repo.GetDataAsync(filter);
        return (result.Items, filter);
    }

    public async Task<IActionResult> ExportCustomerNotPurchasedCsv(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC")
    {
        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null) return RedirectToAction("CustomerNotPurchased");

        var bytes = new CsvExportService().GenerateCustomerNotPurchasedCsv(q.Value.rows, q.Value.filter);
        return File(bytes, "text/csv", $"ItemsNotPurchased_{DateTime.Now:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> ExportCustomerNotPurchasedExcel(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC")
    {
        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null) return RedirectToAction("CustomerNotPurchased");

        var bytes = new ExcelExportService().GenerateCustomerNotPurchasedExcel(q.Value.rows, q.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ItemsNotPurchased_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> ExportCustomerNotPurchasedPdf(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC")
    {
        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null) return RedirectToAction("CustomerNotPurchased");

        var bytes = new PdfExportService().GenerateCustomerNotPurchasedPdf(q.Value.rows, q.Value.filter);
        return File(bytes, "application/pdf", $"ItemsNotPurchased_{DateTime.Now:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> CustomerNotPurchasedPrintPreview(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC")
    {
        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null) return RedirectToAction("CustomerNotPurchased");

        var model = new ViewModels.CustomerNotPurchasedViewModel
        {
            Rows = q.Value.rows,
            Filter = q.Value.filter,
            ConnectedDatabase = GetConnectedDatabaseName()
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SendCustomerNotPurchasedReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC")
    {
        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;
        var stamp = DateTime.Now.ToString("yyyyMMdd");

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateCustomerNotPurchasedPdf(q.Value.rows, q.Value.filter);
                fileName = $"ItemsNotPurchased_{stamp}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateCustomerNotPurchasedCsv(q.Value.rows, q.Value.filter);
                fileName = $"ItemsNotPurchased_{stamp}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateCustomerNotPurchasedExcel(q.Value.rows, q.Value.filter);
                fileName = $"ItemsNotPurchased_{stamp}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
        var rowCount = q.Value.rows.Count;

        var selectionLines = new List<string>
        {
            $"Not purchased for: {days} day(s)",
            $"As at: {q.Value.filter.ReferenceDate:yyyy-MM-dd}",
            $"Group By: {(q.Value.filter.GroupBy == GroupByType.Customer ? "Customer & Item" : "Item")}"
        };
        if (includeNeverPurchased) selectionLines.Add("Include never purchased: Yes");

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Items Not Purchased", dbName, period, rowCount, exportFormat, userName, "Items", selectionsHtml);
        var defaultTextBody = $"Items Not Purchased Report\nDatabase: {dbName}\nPeriod: {period}\nItems: {rowCount}\nFormat: {exportFormat}\n\n{string.Join("\n", selectionLines)}";

        var tokens = BuildEmailTokens("Items Not Purchased", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "CustomerNotPurchased", templateId,
            fileBytes, fileName, contentType,
            $"Items Not Purchased Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeCustomerNotPurchasedReport(
        DateTime dateFrom, DateTime dateTo, DateTime? referenceDate = null,
        int days = 30, string groupBy = "Item", bool includeNeverPurchased = false,
        string? customerCodes = null, bool customerExcludeMode = false,
        string? storeCodes = null, string? itemsSelectionJson = null,
        string sortColumn = "DaysSinceLastPurchase", string sortDirection = "DESC",
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var q = await RunCnpExportQuery(dateFrom, dateTo, referenceDate, days, groupBy, includeNeverPurchased,
            customerCodes, customerExcludeMode, storeCodes, itemsSelectionJson, sortColumn, sortDirection);
        if (q == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (q.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvBytes = new CsvExportService().GenerateCustomerNotPurchasedCsv(q.Value.rows, q.Value.filter);
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
                "AI analysis [CustomerNotPurchased]: {Rows} rows, {CsvLen} chars, locale={Locale}, user={User}",
                q.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var guard = await AnalyzeWithBudgetAsync(csvData, "CustomerNotPurchased", locale, customPrompt, GetTenantConnectionString());
            var guardFail = AiGuardFailure(guard);
            if (guardFail != null) return guardFail;

            return Json(new { success = true, analysis = guard.Analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Items Not Purchased report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveCnpSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCustomerNotPurchased))
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
                ReportType = ReportTypeConstants.CustomerNotPurchased,
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
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.CustomerNotPurchased);
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
            _logger.LogError(ex, "Error saving CustomerNotPurchased schedule");
            return Json(new { success = false, message = "Failed to save schedule." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCnpSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database" });

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.CustomerNotPurchased);
            return Json(new { success = true, schedules });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CustomerNotPurchased schedules");
            return Json(new { success = false, message = "Failed to load schedules." });
        }
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Cancel Log", dbName, period, rowCount, exportFormat, userName, "Rows", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Cancel Log Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {rowCount}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Cancel Log", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "CancelLog", templateId,
            fileBytes, fileName, contentType,
            $"Cancel Log Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
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

            var guardCL = await AnalyzeWithBudgetAsync(csvData, "CancelLog", locale, customPrompt, GetTenantConnectionString());
            var guardFailCL = AiGuardFailure(guardCL);
            if (guardFailCL != null) return guardFailCL;
            var analysis = guardCL.Analysis;

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Cancel Log report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Trial Balance ====================

    public async Task<IActionResult> TrialBalance()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewTrialBalance))
        {
            _logger.LogWarning("User {User} denied access to TrialBalance (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewTrialBalance);
            return RedirectToAction("AccessDenied", "Account");
        }

        try
        {
            var repo = _repositoryFactory.CreateTrialBalanceRepository(tenantConnString);
            var headers = await repo.GetHeadersAsync();
            ViewBag.HeadersJson = System.Text.Json.JsonSerializer.Serialize(
                headers.Select(h => new { key = h.Key, code = h.Code, name = h.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load COA headers for Trial Balance");
            ViewBag.HeadersJson = "[]";
        }

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderTrialBalance,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { /* first time — no layout */ }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout    = savedLayout;
        ViewBag.CanSchedule    = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleTrialBalance);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetTrialBalanceAccounts(string? headers = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateTrialBalanceRepository(tenantConnString);
            var accounts = await repo.GetAccountsAsync(headers);
            return Json(accounts.Select(a => new { code = a.Code, name = a.Name, headerKey = a.HeaderKey }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Trial Balance accounts");
            return Json(Array.Empty<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetTrialBalanceData(
        DateTime asAt,
        bool includeZeroMovements = false,
        string reportMode = "Detailed",
        string? selectedAccounts = null,
        string? selectedHeaders = null,
        string? suppressedHeaders = null,
        string sortColumn = "AccountCode",
        string sortDirection = "ASC")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = BuildTrialBalanceFilter(asAt, includeZeroMovements, reportMode,
                selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);

            var repo = _repositoryFactory.CreateTrialBalanceRepository(tenantConnString);
            var (rows, fiscalYearFound) = await repo.GenerateAsync(filter);

            if (!fiscalYearFound)
                return Json(new { success = false, message = "The selected date does not fall within a defined fiscal year (tbl_acperiod)." });

            return Json(new { success = true, data = rows, totalRows = rows.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Trial Balance data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveTrialBalanceSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleTrialBalance))
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
                ReportType = ReportTypeConstants.TrialBalance,
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
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.TrialBalance);
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
            _logger.LogError(ex, "Error saving Trial Balance schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTrialBalanceSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.TrialBalance);
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

    private static TrialBalanceFilter BuildTrialBalanceFilter(
        DateTime asAt, bool includeZeroMovements, string reportMode,
        string? selectedAccounts, string? selectedHeaders, string? suppressedHeaders,
        string sortColumn, string sortDirection) => new()
        {
            AsAt = asAt,
            IncludeZeroMovements = includeZeroMovements,
            ReportMode = Enum.TryParse<TrialBalanceReportMode>(reportMode, true, out var rm) ? rm : TrialBalanceReportMode.Detailed,
            SelectedAccounts = selectedAccounts ?? "",
            SelectedHeaders = selectedHeaders ?? "",
            SuppressedHeaders = suppressedHeaders ?? "",
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

    private async Task<(List<TrialBalanceRow> rows, TrialBalanceFilter filter)?> RunTrialBalanceQuery(
        DateTime asAt, bool includeZeroMovements, string reportMode,
        string? selectedAccounts, string? selectedHeaders, string? suppressedHeaders,
        string sortColumn, string sortDirection)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = BuildTrialBalanceFilter(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);

        try
        {
            var repo = _repositoryFactory.CreateTrialBalanceRepository(tenantConnString);
            var (rows, _) = await repo.GenerateAsync(filter);
            return (rows, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Trial Balance query");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportTrialBalanceCsv(
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("TrialBalance");

        var bytes = new CsvExportService().GenerateTrialBalanceCsv(result.Value.rows, result.Value.filter);
        return File(bytes, "text/csv", $"TrialBalance_{asAt:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportTrialBalanceExcel(
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("TrialBalance");

        var bytes = new ExcelExportService().GenerateTrialBalanceExcel(result.Value.rows, result.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"TrialBalance_{asAt:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportTrialBalancePdf(
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("TrialBalance");

        var bytes = new PdfExportService().GenerateTrialBalancePdf(result.Value.rows, result.Value.filter);
        return File(bytes, "application/pdf", $"TrialBalance_{asAt:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> TrialBalancePrintPreview(
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("TrialBalance");

        var model = new ViewModels.TrialBalanceViewModel
        {
            AsAt = asAt,
            IncludeZeroMovements = includeZeroMovements,
            ReportMode = reportMode,
            SortColumn = sortColumn,
            SortDirection = sortDirection,
            ConnectedDatabase = GetConnectedDatabaseName(),
            Rows = result.Value.rows
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SendTrialBalanceReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateTrialBalancePdf(result.Value.rows, result.Value.filter);
                fileName = $"TrialBalance_{asAt:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateTrialBalanceCsv(result.Value.rows, result.Value.filter);
                fileName = $"TrialBalance_{asAt:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateTrialBalanceExcel(result.Value.rows, result.Value.filter);
                fileName = $"TrialBalance_{asAt:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"As at {asAt:dd/MM/yyyy}";
        var rowCount = result.Value.rows.Count;

        var selectionLines = new List<string>
        {
            $"As At: {asAt:dd/MM/yyyy}",
            $"Report Mode: {reportMode}",
            $"Include Zero Movements: {(includeZeroMovements ? "Yes" : "No")}"
        };

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Trial Balance", dbName, period, rowCount, exportFormat, userName, "Accounts", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Trial Balance Report\nDatabase: {dbName}\nPeriod: {period}\nAccounts: {rowCount}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Trial Balance", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "TrialBalance", templateId,
            fileBytes, fileName, contentType,
            $"Trial Balance Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeTrialBalanceReport(
        DateTime asAt, bool includeZeroMovements = false, string reportMode = "Detailed",
        string? selectedAccounts = null, string? selectedHeaders = null, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC",
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunTrialBalanceQuery(asAt, includeZeroMovements, reportMode,
            selectedAccounts, selectedHeaders, suppressedHeaders, sortColumn, sortDirection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvBytes = new CsvExportService().GenerateTrialBalanceCsv(result.Value.rows, result.Value.filter);
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
                "AI analysis [TrialBalance]: {Rows} rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var guardTb = await AnalyzeWithBudgetAsync(csvData, "TrialBalance", locale, customPrompt, GetTenantConnectionString());
            var guardFailTb = AiGuardFailure(guardTb);
            if (guardFailTb != null) return guardFailTb;
            var analysis = guardTb.Analysis;

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Trial Balance report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Profit & Loss ====================

    public async Task<IActionResult> ProfitLoss()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewProfitLoss))
        {
            _logger.LogWarning("User {User} denied access to ProfitLoss (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewProfitLoss);
            return RedirectToAction("AccessDenied", "Account");
        }

        try
        {
            var repo = _repositoryFactory.CreateProfitLossRepository(tenantConnString);
            var headers = await repo.GetHeadersAsync();
            ViewBag.HeadersJson = System.Text.Json.JsonSerializer.Serialize(
                headers.Select(h => new { key = h.Key, code = h.Code, name = h.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load COA headers for Profit & Loss");
            ViewBag.HeadersJson = "[]";
        }

        ViewBag.ConnectedDatabase = GetConnectedDatabaseName();

        bool hasSavedLayout = false;
        Dictionary<string, string>? savedLayout = null;
        try
        {
            var iniRepo = _repositoryFactory.CreateIniRepository(tenantConnString);
            savedLayout = await iniRepo.GetLayoutAsync(
                ModuleConstants.ModuleCode,
                ModuleConstants.IniHeaderProfitLoss,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { /* first time — no layout */ }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout    = savedLayout;
        ViewBag.CanSchedule    = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleProfitLoss);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetProfitLossData(
        DateTime dateFrom, DateTime dateTo,
        bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0,
        string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = BuildProfitLossFilter(dateFrom, dateTo, headerLevel, compareToLastYear,
                openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);

            if (!filter.IsValid())
                return Json(new { success = false, message = "From date must be on or before To date." });

            var repo = _repositoryFactory.CreateProfitLossRepository(tenantConnString);
            var (rows, configError) = await repo.GenerateAsync(filter);

            if (configError != null)
                return Json(new { success = false, message = configError });

            var vm = BuildProfitLossViewModel(filter, rows);

            return Json(new
            {
                success = true,
                data = rows,
                totalRows = rows.Count(r => !r.Suppressed),
                compareToLastYear,
                totals = new
                {
                    sales = vm.TotalSales, costOfSales = vm.TotalCostOfSales,
                    income = vm.TotalIncome, expenses = vm.TotalExpenses,
                    grossProfit = vm.GrossProfit, netProfit = vm.NetProfit,
                    priorSales = vm.PriorSales, priorCostOfSales = vm.PriorCostOfSales,
                    priorIncome = vm.PriorIncome, priorExpenses = vm.PriorExpenses,
                    priorGrossProfit = vm.PriorGrossProfit, priorNetProfit = vm.PriorNetProfit
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Profit & Loss data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveProfitLossSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleProfitLoss))
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
                ReportType = ReportTypeConstants.ProfitLoss,
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
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.ProfitLoss);
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
            _logger.LogError(ex, "Error saving Profit & Loss schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetProfitLossSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.ProfitLoss);
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

    private static ProfitLossFilter BuildProfitLossFilter(
        DateTime dateFrom, DateTime dateTo, bool headerLevel, bool compareToLastYear,
        decimal openingStockValue, decimal closingStockValue,
        string? suppressedHeaders, string sortColumn, string sortDirection) => new()
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            HeaderLevel = headerLevel,
            CompareToLastYear = compareToLastYear,
            OpeningStockValue = openingStockValue,
            ClosingStockValue = closingStockValue,
            SuppressedHeaders = suppressedHeaders ?? "",
            SortColumn = sortColumn,
            SortDirection = sortDirection
        };

    private ProfitLossViewModel BuildProfitLossViewModel(ProfitLossFilter filter, List<ProfitLossRow> rows) => new()
    {
        DateFrom = filter.DateFrom,
        DateTo = filter.DateTo,
        HeaderLevel = filter.HeaderLevel,
        CompareToLastYear = filter.CompareToLastYear,
        OpeningStockValue = filter.OpeningStockValue,
        ClosingStockValue = filter.ClosingStockValue,
        SuppressedHeaders = filter.SuppressedHeaders,
        SortColumn = filter.SortColumn,
        SortDirection = filter.SortDirection,
        ConnectedDatabase = GetConnectedDatabaseName(),
        Rows = rows
    };

    private async Task<(List<ProfitLossRow> rows, ProfitLossFilter filter)?> RunProfitLossQuery(
        DateTime dateFrom, DateTime dateTo, bool headerLevel, bool compareToLastYear,
        decimal openingStockValue, decimal closingStockValue,
        string? suppressedHeaders, string sortColumn, string sortDirection)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = BuildProfitLossFilter(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);

        try
        {
            var repo = _repositoryFactory.CreateProfitLossRepository(tenantConnString);
            var (rows, _) = await repo.GenerateAsync(filter);
            return (rows, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Profit & Loss query");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportProfitLossCsv(
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("ProfitLoss");

        var bytes = new CsvExportService().GenerateProfitLossCsv(result.Value.rows, result.Value.filter);
        return File(bytes, "text/csv", $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportProfitLossExcel(
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("ProfitLoss");

        var bytes = new ExcelExportService().GenerateProfitLossExcel(result.Value.rows, result.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportProfitLossPdf(
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("ProfitLoss");

        var bytes = new PdfExportService().GenerateProfitLossPdf(result.Value.rows, result.Value.filter);
        return File(bytes, "application/pdf", $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> ProfitLossPrintPreview(
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null) return RedirectToAction("ProfitLoss");

        var model = BuildProfitLossViewModel(result.Value.filter, result.Value.rows);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SendProfitLossReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC")
    {
        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateProfitLossPdf(result.Value.rows, result.Value.filter);
                fileName = $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateProfitLossCsv(result.Value.rows, result.Value.filter);
                fileName = $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateProfitLossExcel(result.Value.rows, result.Value.filter);
                fileName = $"ProfitLoss_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:dd/MM/yyyy} \u2014 {dateTo:dd/MM/yyyy}";
        var rowCount = result.Value.rows.Count(r => !r.Suppressed);

        var selectionLines = new List<string>
        {
            $"Period: {period}",
            $"Header Level: {(headerLevel ? "Yes" : "No")}",
            $"Compare To Last Year: {(compareToLastYear ? "Yes" : "No")}"
        };

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Profit & Loss", dbName, period, rowCount, exportFormat, userName, "Accounts", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Profit & Loss Report\nDatabase: {dbName}\nPeriod: {period}\nAccounts: {rowCount}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Profit & Loss", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "ProfitLoss", templateId,
            fileBytes, fileName, contentType,
            $"Profit & Loss Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeProfitLossReport(
        DateTime dateFrom, DateTime dateTo, bool headerLevel = false, bool compareToLastYear = false,
        decimal openingStockValue = 0, decimal closingStockValue = 0, string? suppressedHeaders = null,
        string sortColumn = "AccountCode", string sortDirection = "ASC",
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunProfitLossQuery(dateFrom, dateTo, headerLevel, compareToLastYear,
            openingStockValue, closingStockValue, suppressedHeaders, sortColumn, sortDirection);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.rows.Count == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvBytes = new CsvExportService().GenerateProfitLossCsv(result.Value.rows, result.Value.filter);
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
                "AI analysis [ProfitLoss]: {Rows} rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.rows.Count, csvData.Length, locale, User.Identity?.Name);

            var guardPl = await AnalyzeWithBudgetAsync(csvData, "ProfitLoss", locale, customPrompt, GetTenantConnectionString());
            var guardFailPl = AiGuardFailure(guardPl);
            if (guardFailPl != null) return guardFailPl;
            var analysis = guardPl.Analysis;

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Profit & Loss report with AI");
            return Json(new { success = false, message = $"Analysis failed: {ex.Message}" });
        }
    }

    // ==================== Cash Flow (Direct) ====================
    // Engine mirrors Power BI (GetAllTransactionsForBowerBI slice) but self-contained; statement
    // grouping = dboReportsAI.tbl_CashFlowMapping (Operating/Investing/Financing/Other/Bank code
    // ranges, extracted 1:1 from the PBI Accounting model). See CashFlowRepository remarks.

    public async Task<IActionResult> CashFlow()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return RedirectToAction("Index", "Home");

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionViewCashFlow))
        {
            _logger.LogWarning("User {User} denied access to CashFlow (action {Action})",
                User.Identity?.Name, ModuleConstants.ActionViewCashFlow);
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
                ModuleConstants.IniHeaderCashFlow,
                GetUserCode());
            hasSavedLayout = savedLayout.Count > 0;
        }
        catch { /* first time — no layout */ }

        ViewBag.HasSavedLayout = hasSavedLayout;
        ViewBag.SavedLayout    = savedLayout;
        ViewBag.CanSchedule    = await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCashFlow);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetCashFlowData(
        DateTime dateFrom, DateTime dateTo,
        bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected to database." });

        try
        {
            var filter = BuildCashFlowFilter(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);

            if (filter.DateFrom > filter.DateTo)
                return Json(new { success = false, message = "From date must be on or before To date." });
            if (!filter.IsValid())
                return Json(new { success = false, message = "Monthly breakdown supports up to 12 months — please narrow the period." });

            var repo = _repositoryFactory.CreateCashFlowRepository(tenantConnString);
            var result = await repo.GenerateAsync(filter);
            var statement = CashFlowStatementBuilder.Build(result, filter);

            return Json(new
            {
                success = true,
                statement,
                totalRows = statement.AccountRowCount,
                compareToLastYear = filter.CompareToLastYear,
                includeBudget = filter.IncludeBudget,
                showAccounts = filter.ShowAccounts,
                monthly = filter.Monthly
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Cash Flow data");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveCashFlowSchedule(
        string scheduleName, string recurrenceType, int? recurrenceDay,
        string scheduleTime, string exportFormat, string recipients,
        string? emailSubject, string? parametersJson, string? recurrenceJson,
        bool includeAiAnalysis = false, string? aiLocale = "el",
        bool skipIfEmpty = false, int scheduleId = 0)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (!await IsActionAuthorizedAsync(ModuleConstants.ActionScheduleCashFlow))
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
                ReportType = ReportTypeConstants.CashFlow,
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
                var (ok, message) = ValidateScheduleForMutation(existing, ReportTypeConstants.CashFlow);
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
            _logger.LogError(ex, "Error saving Cash Flow schedule");
            return Json(new { success = false, message = "Failed to save schedule. The schedule tables may not exist yet." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCashFlowSchedules()
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(Array.Empty<object>());

        try
        {
            var repo = _repositoryFactory.CreateScheduleRepository(tenantConnString);
            var schedules = await repo.GetSchedulesForReportAsync(ReportTypeConstants.CashFlow);
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

    /// <summary>Monthly mode has no prior-year/budget columns (mirrors the PBI Monthly page) — forced off here so every endpoint behaves identically.</summary>
    private static CashFlowFilter BuildCashFlowFilter(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear, bool includeBudget,
        bool showAccounts, bool monthly) => new()
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            CompareToLastYear = compareToLastYear && !monthly,
            IncludeBudget = includeBudget && !monthly,
            ShowAccounts = showAccounts,
            Monthly = monthly
        };

    private CashFlowViewModel BuildCashFlowViewModel(CashFlowFilter filter, CashFlowStatement statement) => new()
    {
        DateFrom = filter.DateFrom,
        DateTo = filter.DateTo,
        CompareToLastYear = filter.CompareToLastYear,
        IncludeBudget = filter.IncludeBudget,
        ShowAccounts = filter.ShowAccounts,
        Monthly = filter.Monthly,
        ConnectedDatabase = GetConnectedDatabaseName(),
        Statement = statement
    };

    private async Task<(CashFlowStatement statement, CashFlowFilter filter)?> RunCashFlowQuery(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear, bool includeBudget,
        bool showAccounts, bool monthly)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString)) return null;

        var filter = BuildCashFlowFilter(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (!filter.IsValid()) return null;

        try
        {
            var repo = _repositoryFactory.CreateCashFlowRepository(tenantConnString);
            var result = await repo.GenerateAsync(filter);
            return (CashFlowStatementBuilder.Build(result, filter), filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Cash Flow query");
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportCashFlowCsv(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null) return RedirectToAction("CashFlow");

        var bytes = new CsvExportService().GenerateCashFlowCsv(result.Value.statement, result.Value.filter);
        return File(bytes, "text/csv", $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCashFlowExcel(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null) return RedirectToAction("CashFlow");

        var bytes = new ExcelExportService().GenerateCashFlowExcel(result.Value.statement, result.Value.filter);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCashFlowPdf(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null) return RedirectToAction("CashFlow");

        var bytes = new PdfExportService().GenerateCashFlowPdf(result.Value.statement, result.Value.filter);
        return File(bytes, "application/pdf", $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> CashFlowPrintPreview(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null) return RedirectToAction("CashFlow");

        var model = BuildCashFlowViewModel(result.Value.filter, result.Value.statement);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SendCashFlowReportEmail(
        string recipients, string? cc, string? bcc, string? emailSubject,
        string exportFormat, int? templateId,
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false)
    {
        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data." });

        var format = (exportFormat ?? "Excel").ToLowerInvariant();
        byte[] fileBytes;
        string fileName;
        string contentType;

        switch (format)
        {
            case "pdf":
                fileBytes = new PdfExportService().GenerateCashFlowPdf(result.Value.statement, result.Value.filter);
                fileName = $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                contentType = "application/pdf";
                break;
            case "csv":
                fileBytes = new CsvExportService().GenerateCashFlowCsv(result.Value.statement, result.Value.filter);
                fileName = $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                contentType = "text/csv";
                break;
            default:
                fileBytes = new ExcelExportService().GenerateCashFlowExcel(result.Value.statement, result.Value.filter);
                fileName = $"CashFlow_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }

        var dbName = GetConnectedDatabaseName() ?? "Unknown";
        var userName = User.Identity?.Name ?? "Unknown";
        var period = $"{dateFrom:dd/MM/yyyy} \u2014 {dateTo:dd/MM/yyyy}";
        var rowCount = result.Value.statement.AccountRowCount;

        var selectionLines = new List<string>
        {
            $"Period: {period}",
            $"Monthly Breakdown: {(result.Value.filter.Monthly ? "Yes" : "No")}",
            $"Show Accounts: {(showAccounts ? "Yes" : "No")}",
            $"Compare To Last Year: {(result.Value.filter.CompareToLastYear ? "Yes" : "No")}",
            $"Include Budget: {(result.Value.filter.IncludeBudget ? "Yes" : "No")}"
        };

        var selectionsHtml = string.Join("", selectionLines.Select(s =>
            $"<tr><td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280;font-size:12px;'>{s.Split(':')[0]}</td>" +
            $"<td style='padding:4px 12px;border-bottom:1px solid #f3f4f6;font-size:12px;'>{(s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Trim() : "")}</td></tr>"));

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Cash Flow", dbName, period, rowCount, exportFormat, userName, "Lines", selectionsHtml);
        var selectionsText = string.Join("\n", selectionLines);
        var defaultTextBody = $"Cash Flow Report\nDatabase: {dbName}\nPeriod: {period}\nLines: {rowCount}\nFormat: {exportFormat}\n\nSelections:\n{selectionsText}";

        var tokens = BuildEmailTokens("Cash Flow", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "CashFlow", templateId,
            fileBytes, fileName, contentType,
            $"Cash Flow Report \u2014 {period}", defaultHtmlBody, defaultTextBody, tokens);
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeCashFlowReport(
        DateTime dateFrom, DateTime dateTo, bool compareToLastYear = false, bool includeBudget = false,
        bool showAccounts = false, bool monthly = false,
        string? locale = "el", int? promptTemplateId = null)
    {
        if (!_analyzerFactory.IsConfigured)
            return Json(new { success = false, message = "AI Analyzer is not configured. Please set the API key in Settings > AI Analyzer." });

        var result = await RunCashFlowQuery(dateFrom, dateTo, compareToLastYear, includeBudget, showAccounts, monthly);
        if (result == null)
            return Json(new { success = false, message = "Failed to generate report data for analysis." });

        if (result.Value.statement.AccountRowCount == 0)
            return Json(new { success = false, message = "No data to analyze. Please generate the report first." });

        try
        {
            var csvBytes = new CsvExportService().GenerateCashFlowCsv(result.Value.statement, result.Value.filter);
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
                "AI analysis [CashFlow]: {Rows} rows, {CsvLen} chars, locale={Locale}, user={User}",
                result.Value.statement.AccountRowCount, csvData.Length, locale, User.Identity?.Name);

            var guardCf = await AnalyzeWithBudgetAsync(csvData, "CashFlow", locale, customPrompt, GetTenantConnectionString());
            var guardFailCf = AiGuardFailure(guardCf);
            if (guardFailCf != null) return guardFailCf;
            var analysis = guardCf.Analysis;

            return Json(new { success = true, analysis, csvPreview = TruncateCsvForChat(csvData) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Cash Flow report with AI");
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
        string exportFormat, int? templateId,
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Prospect Clients", dbName, period, rowCount, exportFormat, userName, "Records");

        var defaultTextBody = $"Prospect Clients Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {rowCount}\nFormat: {exportFormat}";

        var tokens = BuildEmailTokens("Prospect Clients", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "ProspectClients", templateId,
            fileBytes, fileName, contentType,
            $"Prospect Clients Report \u2014 {period} ({dbName})", defaultHtmlBody, defaultTextBody, tokens);
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

            var guardPC = await AnalyzeWithBudgetAsync(csvData, "ProspectClients", locale, customPrompt, GetTenantConnectionString());
            var guardFailPC = AiGuardFailure(guardPC);
            if (guardFailPC != null) return guardFailPC;
            var analysis = guardPC.Analysis;

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

        try
        {
            var storeRepo = _repositoryFactory.CreateStoreRepository(tenantConnString);
            var stores = await storeRepo.GetActiveStoresAsync();
            ViewBag.StoresJson = System.Text.Json.JsonSerializer.Serialize(
                stores.Select(s => new { code = s.StoreCode, name = s.StoreName }));
        }
        catch { ViewBag.StoresJson = "[]"; }

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
        string exportFormat, int? templateId,
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

        var defaultHtmlBody = BuildDefaultEmailHtmlBody("Offers Report", dbName, period, rowCount, exportFormat, userName, "Records");

        var defaultTextBody = $"Offers Report\nDatabase: {dbName}\nPeriod: {period}\nRows: {rowCount}\nFormat: {exportFormat}";

        var tokens = BuildEmailTokens("Offers Report", dbName, period, rowCount, exportFormat, userName);

        return await SendReportEmailCore(recipients, cc, bcc, emailSubject, "OffersReport", templateId,
            fileBytes, fileName, contentType,
            $"Offers Report \u2014 {period} ({dbName})", defaultHtmlBody, defaultTextBody, tokens);
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

            var guardOR = await AnalyzeWithBudgetAsync(csvData, "OffersReport", locale, customPrompt, GetTenantConnectionString());
            var guardFailOR = AiGuardFailure(guardOR);
            if (guardFailOR != null) return guardFailOR;
            var analysis = guardOR.Analysis;

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

    // ─── Email Address Book ────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetEmailRecipients(string? q = null)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, recipients = Array.Empty<object>() });

        try
        {
            var repo = _repositoryFactory.CreateEmailRecipientRepository(tenantConnString);
            var list = string.IsNullOrWhiteSpace(q)
                ? await repo.GetAllAsync()
                : await repo.SearchAsync(q);

            return Json(new
            {
                success = true,
                recipients = list.Select(r => new
                {
                    id = r.RecipientId,
                    email = r.EmailAddress,
                    name = r.DisplayName,
                    label = string.IsNullOrEmpty(r.DisplayName) ? r.EmailAddress : $"{r.DisplayName} <{r.EmailAddress}>"
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading email recipients");
            return Json(new { success = false, recipients = Array.Empty<object>() });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddEmailRecipient([FromForm] string emailAddress, [FromForm] string? displayName)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        if (string.IsNullOrWhiteSpace(emailAddress) || !EmailRegex.IsMatch(emailAddress.Trim()))
            return Json(new { success = false, message = "Invalid email address." });

        try
        {
            var repo = _repositoryFactory.CreateEmailRecipientRepository(tenantConnString);
            var recipient = await repo.AddAsync(emailAddress.Trim(), displayName ?? "", GetUserCode());
            if (recipient == null)
                return Json(new { success = false, message = "Email already exists in address book." });

            return Json(new
            {
                success = true,
                recipient = new
                {
                    id = recipient.RecipientId,
                    email = recipient.EmailAddress,
                    name = recipient.DisplayName,
                    label = string.IsNullOrEmpty(recipient.DisplayName) ? recipient.EmailAddress : $"{recipient.DisplayName} <{recipient.EmailAddress}>"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding email recipient {Email}", emailAddress);
            return Json(new { success = false, message = "Failed to add recipient." });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteEmailRecipient(int id)
    {
        var tenantConnString = GetTenantConnectionString();
        if (string.IsNullOrEmpty(tenantConnString))
            return Json(new { success = false, message = "Not connected." });

        try
        {
            var repo = _repositoryFactory.CreateEmailRecipientRepository(tenantConnString);
            var deleted = await repo.DeleteAsync(id);
            return Json(new { success = deleted, message = deleted ? "Removed from address book." : "Recipient not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting email recipient {Id}", id);
            return Json(new { success = false, message = "Failed to delete recipient." });
        }
    }
}
