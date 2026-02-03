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
        
        try
        {
            var repo = new AverageBasketRepository(tenantConnString);
            model.Results = await repo.GetAverageBasketDataAsync(
                model.DateFrom,
                model.DateTo,
                model.Breakdown
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Average Basket report");
            model.ErrorMessage = $"Error generating report: {ex.Message}";
        }
        
        return View(model);
    }
}
