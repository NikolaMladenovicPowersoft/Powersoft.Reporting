using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ISalesThroughRepository
{
    Task<PagedResult<SalesThroughRow>> GetSalesThroughDataAsync(SalesThroughFilter filter);
    Task<SalesThroughTotals> GetSalesThroughTotalsAsync(SalesThroughFilter filter);
    Task<bool> TestConnectionAsync();
}
