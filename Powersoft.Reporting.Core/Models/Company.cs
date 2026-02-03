namespace Powersoft.Reporting.Core.Models;

public class Company
{
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public bool CompanyActive { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
