using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICustomerNotPurchasedRepository
{
    Task<PagedResult<CustomerNotPurchasedRow>> GetDataAsync(CustomerNotPurchasedFilter filter);
    Task<bool> TestConnectionAsync();
}
