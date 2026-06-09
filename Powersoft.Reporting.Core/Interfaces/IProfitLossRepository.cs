using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IProfitLossRepository
{
    /// <summary>
    /// Builds the Profit &amp; Loss statement for the filter period. Mirrors legacy
    /// Powersoft.CloudReports\ProfitAndLoss.vb + WQR.ProfitAndLoss.
    ///
    /// Returns the per-account (or per-header, when HeaderLevel) rows, and a message that is
    /// non-empty when one or more of the four control headers (SALESHEA, COSTOSHEA, INCHEA,
    /// EXPHEA) is not defined in tbl_acontrol — in which case the statement cannot be built.
    /// </summary>
    Task<(List<ProfitLossRow> rows, string? configError)> GenerateAsync(ProfitLossFilter filter);

    /// <summary>COA headers that descend from one of the four P&amp;L control headers, for the suppress picker.</summary>
    Task<List<ProfitLossHeaderOption>> GetHeadersAsync();
}
