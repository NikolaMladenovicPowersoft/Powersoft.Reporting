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
    Task<List<DimensionItem>> GetPaymentTypesAsync();
    Task<List<DimensionItem>> GetZReportsAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetTownsAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetUsersAsync(string? search = null, int maxResults = 500);
    Task<List<DimensionItem>> GetModelsAsync();
    Task<List<DimensionItem>> GetColoursAsync();
    Task<List<DimensionItem>> GetSizesAsync();
    Task<List<DimensionItem>> GetGroupSizesAsync();
    Task<List<DimensionItem>> GetFabricsAsync();
    Task<List<DimensionItem>> GetAttributeValuesAsync(int attrIndex);
}
