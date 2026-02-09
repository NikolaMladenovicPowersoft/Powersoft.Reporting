using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IStoreRepository
{
    Task<List<Store>> GetActiveStoresAsync();
    Task<List<Store>> GetStoresByCodesAsync(IEnumerable<string> storeCodes);
}
