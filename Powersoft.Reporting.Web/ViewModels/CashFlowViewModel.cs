using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class CashFlowViewModel
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public bool CompareToLastYear { get; set; }
    public bool IncludeBudget { get; set; }
    public bool ShowAccounts { get; set; }
    public bool Monthly { get; set; }

    public string? ConnectedDatabase { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Presentation statement (Group → Category → optional accounts), built by CashFlowStatementBuilder.</summary>
    public CashFlowStatement Statement { get; set; } = new();

    public int TotalCount => Statement.AccountRowCount;
}
