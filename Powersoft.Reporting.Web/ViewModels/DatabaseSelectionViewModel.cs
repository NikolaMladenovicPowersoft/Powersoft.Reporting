using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class DatabaseSelectionViewModel
{
    public List<Company> Companies { get; set; } = new();
    public List<Database> Databases { get; set; } = new();

    /// <summary>
    /// All accessible databases (pre-filtered by ranking + module).
    /// Grouped by CompanyCode on the client side.
    /// </summary>
    public List<Database> AccessibleDatabases { get; set; } = new();

    public string? SelectedCompanyCode { get; set; }
    public string? SelectedDatabaseCode { get; set; }
    public string? ConnectedDatabaseName { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }

    // User context
    public string? UserCode { get; set; }
    public string? DisplayName { get; set; }
    public string? RoleName { get; set; }
    public int Ranking { get; set; }
    public bool IsSystemAdmin => Ranking < 15;
}
