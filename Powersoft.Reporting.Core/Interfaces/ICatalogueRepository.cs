using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICatalogueRepository
{
    Task<PagedResult<CatalogueRow>> GetCatalogueDataAsync(CatalogueFilter filter);
    Task<CatalogueTotals> GetCatalogueTotalsAsync(CatalogueFilter filter);
    Task<ItemStockPositionResult> GetItemStockPositionAsync(string itemCode);
    Task<bool> TestConnectionAsync();
}
