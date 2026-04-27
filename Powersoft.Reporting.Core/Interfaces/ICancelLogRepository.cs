using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICancelLogRepository
{
    Task<(List<CancelLogDetailedRow> rows, int totalRecords)> GetDetailedAsync(CancelLogFilter filter);
    Task<(List<CancelLogSummaryRow> rows, int totalRecords)> GetSummaryAsync(CancelLogFilter filter);
}
