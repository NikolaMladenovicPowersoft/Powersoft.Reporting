using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IBelowMinStockRepository
{
    Task<List<BelowMinStockRow>> GetBelowMinStockAsync(BelowMinStockFilter filter);
}
