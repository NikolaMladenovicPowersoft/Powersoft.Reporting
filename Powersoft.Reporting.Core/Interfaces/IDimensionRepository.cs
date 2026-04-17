using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IDimensionRepository
{
    Task<List<DimensionItem>> GetCategoriesAsync();
    Task<List<DimensionItem>> GetDepartmentsAsync();
    Task<List<DimensionItem>> GetBrandsAsync();
    Task<List<DimensionItem>> GetSeasonsAsync();
    Task<List<DimensionItem>> GetSuppliersAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetCustomersAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetAgentsAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetPostalCodesAsync(string? search = null, int maxResults = 500);
}
