namespace Powersoft.Reporting.Core.Interfaces;

public interface ITenantRepositoryFactory
{
    IStoreRepository CreateStoreRepository(string connectionString);
    IAverageBasketRepository CreateAverageBasketRepository(string connectionString);
}
