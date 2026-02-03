namespace Powersoft.Reporting.Core.Models;

public class Database
{
    public string DBCode { get; set; } = string.Empty;
    public string DBFriendlyName { get; set; } = string.Empty;
    public string DBName { get; set; } = string.Empty;
    public string DBServerID { get; set; } = string.Empty;
    public string? DBProviderInstanceName { get; set; }
    public string? DBUserName { get; set; }
    public string? DBPassword { get; set; }  // Encrypted
    public bool DBActive { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
}
