namespace Powersoft.Reporting.Core.Interfaces;

public interface ITenantRepositoryFactory
{
    IStoreRepository CreateStoreRepository(string connectionString);
    IItemRepository CreateItemRepository(string connectionString);
    IAverageBasketRepository CreateAverageBasketRepository(string connectionString);
    IPurchasesSalesRepository CreatePurchasesSalesRepository(string connectionString);
    IScheduleRepository CreateScheduleRepository(string connectionString);
    IIniRepository CreateIniRepository(string connectionString);
    IChartRepository CreateChartRepository(string connectionString);
    IParetoRepository CreateParetoRepository(string connectionString);
    IDimensionRepository CreateDimensionRepository(string connectionString);
}
