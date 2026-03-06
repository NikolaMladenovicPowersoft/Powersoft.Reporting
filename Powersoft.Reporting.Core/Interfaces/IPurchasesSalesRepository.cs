using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IPurchasesSalesRepository
{
    Task<PagedResult<PurchasesSalesRow>> GetPurchasesSalesDataAsync(PurchasesSalesFilter filter);
    Task<bool> TestConnectionAsync();
}
