using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Data.Tenant;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

public class ReportsController : Controller
{
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(ILogger<ReportsController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        var connectedDb = HttpContext.Session.GetString("ConnectedDatabase");
        if (string.IsNullOrEmpty(connectedDb))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }
        
        return View();
    }

    public IActionResult AverageBasket()
    {
        var connectedDb = HttpContext.Session.GetString("ConnectedDatabase");
        var tenantConnString = HttpContext.Session.GetString("TenantConnectionString");
        
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
        
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> AverageBasket(AverageBasketViewModel model)
    {
        var connectedDb = HttpContext.Session.GetString("ConnectedDatabase");
        var tenantConnString = HttpContext.Session.GetString("TenantConnectionString");
        
        if (string.IsNullOrEmpty(tenantConnString))
        {
            TempData["Warning"] = "Please select and connect to a database first.";
            return RedirectToAction("Index", "Home");
        }
        
        model.ConnectedDatabase = connectedDb;
        model.IsConnected = true;
        
        // Handle date presets
        ApplyDatePreset(model);
        
        try
        {
            var repo = new AverageBasketRepository(tenantConnString);
            model.Results = await repo.GetAverageBasketDataAsync(
                model.DateFrom,
                model.DateTo,
                model.Breakdown,
                model.GroupBy
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Average Basket report");
            model.ErrorMessage = $"Error generating report: {ex.Message}";
        }
        
        return View(model);
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
