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

    public IItemRepository CreateItemRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new ItemRepository(connectionString);
    }

    public IAverageBasketRepository CreateAverageBasketRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new AverageBasketRepository(connectionString);
    }

    public IPurchasesSalesRepository CreatePurchasesSalesRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return new PurchasesSalesRepository(connectionString);
    }

    public IScheduleRepository CreateScheduleRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new ScheduleRepository(connectionString);
    }

    public IIniRepository CreateIniRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new IniRepository(connectionString);
    }

    public IChartRepository CreateChartRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new ChartRepository(connectionString);
    }

    public IParetoRepository CreateParetoRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            
        return new ParetoRepository(connectionString);
    }

    public IDimensionRepository CreateDimensionRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return new DimensionRepository(connectionString);
    }

    public IBelowMinStockRepository CreateBelowMinStockRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return new BelowMinStockRepository(connectionString);
    }

    public ICatalogueRepository CreateCatalogueRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return new CatalogueRepository(connectionString);
    }
}
