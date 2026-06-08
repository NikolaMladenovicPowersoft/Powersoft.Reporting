namespace Powersoft.Reporting.Core.Models;

public class AiTokenBudget
{
    public int BudgetId { get; set; }
    public int MonthlyTokenLimit { get; set; } = 500000;
    public int CurrentMonthUsed { get; set; }
    public DateTime BudgetMonth { get; set; }
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Per-analysis soft cost threshold (USD). When the estimated cost of a single
    /// analysis exceeds this, the user is warned and must confirm before proceeding.
    /// </summary>
    public decimal SoftCostLimit { get; set; } = 0.10m;

    /// <summary>
    /// Per-analysis hard cost cap (USD). Analyses whose estimated cost exceeds this
    /// are blocked outright (no tokens spent). Raise per-customer for clients who
    /// explicitly accept higher cost.
    /// </summary>
    public decimal HardCostLimit { get; set; } = 0.25m;
}
