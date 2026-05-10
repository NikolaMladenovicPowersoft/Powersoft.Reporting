using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IProspectClientsRepository
{
    Task<(List<ProspectClientsRow> rows, int totalRecords)> GetDataAsync(ProspectClientsFilter filter);
}
