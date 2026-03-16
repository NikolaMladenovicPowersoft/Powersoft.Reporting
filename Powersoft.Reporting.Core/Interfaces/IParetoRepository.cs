using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IParetoRepository
{
    Task<ParetoResult> GetParetoDataAsync(ParetoFilter filter);
}
