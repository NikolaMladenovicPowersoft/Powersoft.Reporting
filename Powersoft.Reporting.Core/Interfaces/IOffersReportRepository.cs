using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IOffersReportRepository
{
    Task<(List<OffersReportRow> rows, int totalRecords)> GetDataAsync(OffersReportFilter filter);
}
