using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Helpers;
using Powersoft.Reporting.Web.ViewModels;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ICentralRepository _centralRepository;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ICentralRepository centralRepository, ILogger<HomeController> logger)
    {
        _centralRepository = centralRepository;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var viewModel = new DatabaseSelectionViewModel();
        
        try
        {
            viewModel.Companies = await _centralRepository.GetActiveCompaniesAsync();
            
            var connectedDb = HttpContext.Session.GetString(SessionKeys.ConnectedDatabase);
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
            var databases = await _centralRepository.GetActiveDatabasesForCompanyAsync(companyCode);
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
            var database = await _centralRepository.GetDatabaseByCodeAsync(databaseCode);
            if (database == null)
            {
                return Json(new { success = false, message = "Database not found" });
            }
            
            var tenantConnString = ConnectionStringBuilder.Build(database);
            _logger.LogInformation("Attempting connection to: {Server}\\{Instance}, DB: {DbName}, User: {User}", 
                database.DBServerID, database.DBProviderInstanceName, database.DBName, database.DBUserName);
            
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(tenantConnString);
            await conn.OpenAsync();
            
            HttpContext.Session.SetString(SessionKeys.TenantConnectionString, tenantConnString);
            HttpContext.Session.SetString(SessionKeys.ConnectedDatabase, database.DBFriendlyName);
            HttpContext.Session.SetString(SessionKeys.ConnectedDatabaseCode, database.DBCode);
            
            return Json(new { success = true, databaseName = database.DBFriendlyName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to database {DatabaseCode}", databaseCode);
            return Json(new { success = false, message = "Connection failed. Please try again or contact support." });
        }
    }

    [HttpPost]
    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove(SessionKeys.TenantConnectionString);
        HttpContext.Session.Remove(SessionKeys.ConnectedDatabase);
        HttpContext.Session.Remove(SessionKeys.ConnectedDatabaseCode);
        
        return RedirectToAction("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }
}
