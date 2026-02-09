namespace Powersoft.Reporting.Core.Models;

public class Store
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public bool Active { get; set; }
    
    public string DisplayName => string.IsNullOrEmpty(ShortName) 
        ? $"{StoreCode} - {StoreName}" 
        : $"{ShortName} - {StoreName}";
}
