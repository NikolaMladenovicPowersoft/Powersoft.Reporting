using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class DatabaseSelectionViewModel
{
    public List<Company> Companies { get; set; } = new();
    public List<Database> Databases { get; set; } = new();
    public string? SelectedCompanyCode { get; set; }
    public string? SelectedDatabaseCode { get; set; }
    public string? ConnectedDatabaseName { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
}
