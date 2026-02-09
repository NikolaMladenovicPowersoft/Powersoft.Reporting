using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ITenantRepositoryFactory _repositoryFactory;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(ITenantRepositoryFactory repositoryFactory, ILogger<ReportsController> logger)
    {
        _repositoryFactory = repositoryFactory;
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

    public async Task<IActionResult> AverageBasket()
    {
        var connectedDb = GetConnectedDatabaseName();
        var tenantConnString = GetTenantConnectionString();
        
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }
        
        var viewModel = new AverageBasketViewModel
        {
            ConnectedDatabase = connectedDb,
            IsConnected = true,
            DateFrom = new DateTime(DateTime.Today.Year, 1, 1),
            DateTo = DateTime.Today
        };
        
        await LoadAvailableStoresAsync(viewModel, tenantConnString);
        
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
        
        model.ConnectedDatabase = connectedDb;
        model.IsConnected = true;
        
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
