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
    IBelowMinStockRepository CreateBelowMinStockRepository(string connectionString);
    ICatalogueRepository CreateCatalogueRepository(string connectionString);
    ICancelLogRepository CreateCancelLogRepository(string connectionString);
    IProspectClientsRepository CreateProspectClientsRepository(string connectionString);
    IOffersReportRepository CreateOffersReportRepository(string connectionString);
    ITrialBalanceRepository CreateTrialBalanceRepository(string connectionString);
    IProfitLossRepository CreateProfitLossRepository(string connectionString);
    IEmailRecipientRepository CreateEmailRecipientRepository(string connectionString);
    ICustomerNotPurchasedRepository CreateCustomerNotPurchasedRepository(string connectionString);
    ICashFlowRepository CreateCashFlowRepository(string connectionString);
    ICashFlowMappingRepository CreateCashFlowMappingRepository(string connectionString);
    ISalesThroughRepository CreateSalesThroughRepository(string connectionString);
}
