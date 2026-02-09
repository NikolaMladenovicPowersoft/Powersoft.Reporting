using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IAverageBasketRepository
{
    Task<PagedResult<AverageBasketRow>> GetAverageBasketDataAsync(ReportFilter filter);
    Task<List<AverageBasketRow>> GetAverageBasketDataAsync(
        DateTime dateFrom,
        DateTime dateTo,
        BreakdownType breakdown = BreakdownType.Monthly,
        GroupByType groupBy = GroupByType.None,
        bool includeLastYear = false);
    Task<bool> TestConnectionAsync();
}
