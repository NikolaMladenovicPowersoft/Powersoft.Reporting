using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services.AI;

public interface IReportAnalyzer
{
    bool IsConfigured { get; }
    Task<ReportAnalysis> AnalyzeAsync(string csvData, string reportType, string? locale = null, CancellationToken ct = default);
}
