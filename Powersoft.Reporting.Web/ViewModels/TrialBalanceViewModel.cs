using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class TrialBalanceViewModel
{
    public DateTime AsAt { get; set; } = DateTime.Today;
    public bool IncludeZeroMovements { get; set; }
    public string ReportMode { get; set; } = "Detailed";
    public string SortColumn { get; set; } = "AccountCode";
    public string SortDirection { get; set; } = "ASC";

    public List<TrialBalanceRow> Rows { get; set; } = new();
    public int TotalCount => Rows.Count;

    public string? ConnectedDatabase { get; set; }

    public bool IsSummary => ReportMode.Equals("Summary", StringComparison.OrdinalIgnoreCase);

    // Grand totals (suppressed rows still count, mirroring legacy report totals).
    public decimal TotalOpeningDr => Rows.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance);
    public decimal TotalOpeningCr => Rows.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance);
    public decimal TotalDebit => Rows.Sum(r => r.DebitMovement);
    public decimal TotalCredit => Rows.Sum(r => r.CreditMovement);
    public decimal TotalClosingDr => Rows.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance);
    public decimal TotalClosingCr => Rows.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance);
}
