using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IChartRepository
{
    Task<List<ChartDataPoint>> GetSalesBreakdownAsync(ChartFilter filter);
}
