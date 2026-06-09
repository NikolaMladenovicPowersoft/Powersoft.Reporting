using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.ViewModels;

public class ProfitLossViewModel
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public bool HeaderLevel { get; set; }
    public bool CompareToLastYear { get; set; }
    public decimal OpeningStockValue { get; set; }
    public decimal ClosingStockValue { get; set; }
    public string? SuppressedHeaders { get; set; }
    public string SortColumn { get; set; } = "AccountCode";
    public string SortDirection { get; set; } = "ASC";

    public string? ConnectedDatabase { get; set; }
    public string? ErrorMessage { get; set; }

    public List<ProfitLossRow> Rows { get; set; } = new();

    public int TotalCount => Rows.Count(r => !r.Suppressed);

    // ── Group totals (sign-normalized: positive = favourable) ──
    private decimal GroupTotal(ProfitLossGroup g) =>
        Rows.Where(r => !r.Suppressed && r.Group == g).Sum(r => r.Balance);
    private decimal GroupPriorTotal(ProfitLossGroup g) =>
        Rows.Where(r => !r.Suppressed && r.Group == g).Sum(r => r.PriorBalance);

    public decimal TotalSales        => GroupTotal(ProfitLossGroup.Sales);
    public decimal TotalCostOfSales  => GroupTotal(ProfitLossGroup.CostOfSales);
    public decimal TotalIncome       => GroupTotal(ProfitLossGroup.Income);
    public decimal TotalExpenses     => GroupTotal(ProfitLossGroup.Expenses);

    public decimal PriorSales        => GroupPriorTotal(ProfitLossGroup.Sales);
    public decimal PriorCostOfSales  => GroupPriorTotal(ProfitLossGroup.CostOfSales);
    public decimal PriorIncome       => GroupPriorTotal(ProfitLossGroup.Income);
    public decimal PriorExpenses     => GroupPriorTotal(ProfitLossGroup.Expenses);

    // ── Profit lines (legacy: Gross = Sales - Cost; Net = Gross + Income - Expenses) ──
    public decimal GrossProfit       => TotalSales - TotalCostOfSales;
    public decimal NetProfit         => GrossProfit + TotalIncome - TotalExpenses;
    public decimal PriorGrossProfit  => PriorSales - PriorCostOfSales;
    public decimal PriorNetProfit    => PriorGrossProfit + PriorIncome - PriorExpenses;
}
