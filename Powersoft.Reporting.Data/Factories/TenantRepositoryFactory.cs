using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Tenant;

namespace Powersoft.Reporting.Data.Factories;

public class TenantRepositoryFactory : ITenantRepositoryFactory
{
    public IStoreRepository CreateStoreRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new StoreRepository(connectionString);
    }

    public IAverageBasketRepository CreateAverageBasketRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new AverageBasketRepository(connectionString);
    }
}
