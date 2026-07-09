using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICashFlowRepository
{
    /// <summary>
    /// Returns the cash-flow account rows for the period, each mapped to its statement
    /// Group/Category (dboReportsAI.tbl_CashFlowMapping), plus the full mapping skeleton so the
    /// presentation layer can render every statement line (empty ones as "-", like the PBI matrix).
    /// </summary>
    Task<CashFlowResult> GenerateAsync(CashFlowFilter filter);
}
