namespace Powersoft.Reporting.Core.Models;

public class AiTokenBudget
{
    public int BudgetId { get; set; }
    public int MonthlyTokenLimit { get; set; } = 500000;
    public int CurrentMonthUsed { get; set; }
    public DateTime BudgetMonth { get; set; }
    public DateTime LastUpdated { get; set; }
}
