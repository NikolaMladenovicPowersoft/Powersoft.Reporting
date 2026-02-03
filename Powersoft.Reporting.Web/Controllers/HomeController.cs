using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Data.Central;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

public class HomeController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IConfiguration configuration, ILogger<HomeController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var viewModel = new DatabaseSelectionViewModel();
        
        try
        {
            var centralConnString = _configuration.GetConnectionString("PSCentral");
            var repo = new CentralRepository(centralConnString!);
            
            viewModel.Companies = await repo.GetActiveCompaniesAsync();
            
            // Check if already connected
            var connectedDb = HttpContext.Session.GetString("ConnectedDatabase");
            if (!string.IsNullOrEmpty(connectedDb))
            {
                viewModel.ConnectedDatabaseName = connectedDb;
                viewModel.IsConnected = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading companies");
            viewModel.ErrorMessage = "Unable to connect to PS Central database. Please check configuration.";
        }
        
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> GetDatabases(string companyCode)
    {
        try
        {
            var centralConnString = _configuration.GetConnectionString("PSCentral");
            var repo = new CentralRepository(centralConnString!);
            
            var databases = await repo.GetActiveDatabasesForCompanyAsync(companyCode);
            return Json(databases.Select(d => new { value = d.DBCode, text = d.DBFriendlyName }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading databases for company {CompanyCode}", companyCode);
            return Json(new { error = "Failed to load databases" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Connect(string databaseCode)
    {
        try
        {
            var centralConnString = _configuration.GetConnectionString("PSCentral");
            var repo = new CentralRepository(centralConnString!);
            
            var database = await repo.GetDatabaseByCodeAsync(databaseCode);
            if (database == null)
            {
                return Json(new { success = false, message = "Database not found" });
            }
            
            // Build tenant connection string
            var tenantConnString = ConnectionStringBuilder.Build(database);
            
            // Test connection
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
            await conn.OpenAsync();
            
            // Store in session
            HttpContext.Session.SetString("TenantConnectionString", tenantConnString);
            HttpContext.Session.SetString("ConnectedDatabase", database.DBFriendlyName);
            HttpContext.Session.SetString("ConnectedDatabaseCode", database.DBCode);
            
            return Json(new { success = true, databaseName = database.DBFriendlyName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to database {DatabaseCode}", databaseCode);
            return Json(new { success = false, message = $"Connection failed: {ex.Message}" });
        }
    }

    [HttpPost]
    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove("TenantConnectionString");
        HttpContext.Session.Remove("ConnectedDatabase");
        HttpContext.Session.Remove("ConnectedDatabaseCode");
        
        return RedirectToAction("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }
}
