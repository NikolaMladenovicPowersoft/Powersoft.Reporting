using System.Globalization;
using System.Text;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services;

public class CsvExportService
{
    public byte[] GenerateAverageBasketCsv(
        List<AverageBasketRow> rows,
        ReportGrandTotals? grandTotals,
        ReportFilter filter)
    {
        bool hasGrouping = filter.GroupBy != Core.Enums.GroupByType.None;
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;

        var sb = new StringBuilder();

        sb.AppendLine($"# Average Basket Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Breakdown: {filter.Breakdown}");
        sb.AppendLine($"# Group By: {filter.GroupBy}");
        if (filter.SecondaryGroupBy != Core.Enums.GroupByType.None)
            sb.AppendLine($"# Secondary Group: {filter.SecondaryGroupBy}");
        sb.AppendLine($"# Include VAT: {(includeVat ? "Yes" : "No")}");
        if (compareLY) sb.AppendLine("# Compare Last Year: Yes");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        var headers = new List<string>();
        if (hasGrouping) headers.Add(filter.GroupBy.ToString());
        headers.AddRange(new[]
        {
            "Period", "Invoices", "Returns", "Net Trans.",
            "Qty Sold", "Qty Ret.", "Net Qty",
            includeVat ? "Gross Sales" : "Net Sales",
            "Avg Basket", "Avg Qty"
        });
        if (compareLY)
        {
            headers.AddRange(new[] { "LY Sales", "LY Avg", "YoY %" });
        }
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        // Data rows
        foreach (var row in rows)
        {
            var cells = new List<string>();
            if (hasGrouping) cells.Add(row.Level1Value ?? row.Level1 ?? "N/A");

            cells.Add(row.Period);
            cells.Add(row.CYInvoiceCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYCreditCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYTotalTransactions.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYQtySold.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYQtyReturned.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYTotalQty.ToString(CultureInfo.InvariantCulture));
            cells.Add((includeVat ? row.CYTotalGross : row.CYTotalNet).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add((includeVat ? row.CYAverageGross : row.CYAverageNet).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.CYAverageQty.ToString("F2", CultureInfo.InvariantCulture));

            if (compareLY)
            {
                cells.Add((includeVat ? row.LYTotalGross : row.LYTotalNet).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add((includeVat ? row.LYAverageGross : row.LYAverageNet).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.YoYChangePercent.ToString("F1", CultureInfo.InvariantCulture));
            }

            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        // Grand total row
        if (grandTotals != null)
        {
            var cells = new List<string>();
            if (hasGrouping) cells.Add("");

            cells.Add("GRAND TOTAL");
            cells.Add(grandTotals.TotalInvoices.ToString(CultureInfo.InvariantCulture));
            cells.Add(grandTotals.TotalCredits.ToString(CultureInfo.InvariantCulture));
            cells.Add(grandTotals.NetTransactions.ToString(CultureInfo.InvariantCulture));
            cells.Add(grandTotals.TotalQtySold.ToString(CultureInfo.InvariantCulture));
            cells.Add(grandTotals.TotalQtyReturned.ToString(CultureInfo.InvariantCulture));
            cells.Add(grandTotals.NetQty.ToString(CultureInfo.InvariantCulture));
            cells.Add((includeVat ? grandTotals.GrossSales : grandTotals.NetSales).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add((includeVat ? grandTotals.AverageBasketGross : grandTotals.AverageBasketNet).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(grandTotals.AverageQty.ToString("F2", CultureInfo.InvariantCulture));

            if (compareLY)
            {
                cells.Add((includeVat ? grandTotals.LYTotalGross : grandTotals.LYTotalNet).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add((includeVat ? grandTotals.LYAverageBasketGross : grandTotals.LYAverageBasketNet).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(grandTotals.YoYChangePercent.ToString("F1", CultureInfo.InvariantCulture));
            }

            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    public byte[] GeneratePurchasesSalesCsv(
        List<PurchasesSalesRow> rows,
        PurchasesSalesTotals? totals,
        PurchasesSalesFilter filter)
    {
        bool hasL1 = filter.PrimaryGroup != Core.Enums.PsGroupBy.None;
        bool hasL2 = filter.SecondaryGroup != Core.Enums.PsGroupBy.None;
        bool hasL3 = filter.ThirdGroup != Core.Enums.PsGroupBy.None;
        bool hasItem = !filter.IsSummary || (!hasL1 && !hasL2 && !hasL3);

        var sb = new StringBuilder();

        sb.AppendLine($"# Purchases vs Sales Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Mode: {filter.ReportMode}");
        if (hasL1) sb.AppendLine($"# Primary Group: {filter.PrimaryGroup}");
        if (hasL2) sb.AppendLine($"# Secondary Group: {filter.SecondaryGroup}");
        if (hasL3) sb.AppendLine($"# Third Group: {filter.ThirdGroup}");
        sb.AppendLine($"# Include VAT: {(filter.IncludeVat ? "Yes" : "No")}");
        if (filter.ShowProfit) sb.AppendLine("# Show Profit: Yes");
        if (filter.ShowStock) sb.AppendLine("# Show Stock: Yes");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        var headers = new List<string>();
        if (hasL1) headers.Add(filter.PrimaryGroup.ToString());
        if (hasL2) headers.Add(filter.SecondaryGroup.ToString());
        if (hasL3) headers.Add(filter.ThirdGroup.ToString());
        if (hasItem) { headers.Add("Item Code"); headers.Add("Item Name"); }
        headers.AddRange(new[]
        {
            "Qty Purchased", filter.IncludeVat ? "Gross Purchased" : "Net Purchased",
            "Qty Sold", filter.IncludeVat ? "Gross Sold" : "Net Sold",
            "Profit", "Qty %", "Val %"
        });
        if (filter.ShowStock) headers.Add("Stock Qty");
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        string? prevL1 = null, prevL2 = null, prevL3 = null;
        var l1Agg = new SubtotalAgg(); var l2Agg = new SubtotalAgg(); var l3Agg = new SubtotalAgg();

        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            string curL1 = hasL1 ? (row.Level1Value ?? row.Level1 ?? "N/A") : "";
            string curL2 = hasL2 ? (row.Level2Value ?? row.Level2 ?? "N/A") : "";
            string curL3 = hasL3 ? (row.Level3Value ?? row.Level3 ?? "N/A") : "";

            bool l1Changed = hasL1 && curL1 != prevL1 && ri > 0;
            bool l2Changed = hasL2 && (curL2 != prevL2 || l1Changed) && ri > 0;
            bool l3Changed = hasL3 && (curL3 != prevL3 || l2Changed) && ri > 0;

            if (l3Changed) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL3}", l3Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (l2Changed) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL2}", l2Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (l1Changed) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL1}", l1Agg, filter, hasL1, hasL2, hasL3, hasItem);

            if (l1Changed || ri == 0) { l1Agg = new SubtotalAgg(); l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l2Changed) { l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l3Changed) { l3Agg = new SubtotalAgg(); }
            prevL1 = curL1; prevL2 = curL2; prevL3 = curL3;

            l1Agg.Add(row, filter.IncludeVat); l2Agg.Add(row, filter.IncludeVat); l3Agg.Add(row, filter.IncludeVat);

            var cells = new List<string>();
            if (hasL1) cells.Add(curL1);
            if (hasL2) cells.Add(curL2);
            if (hasL3) cells.Add(curL3);
            if (hasItem) { cells.Add(row.ItemCode ?? ""); cells.Add(row.ItemName ?? ""); }
            cells.Add(row.QuantityPurchased.ToString(CultureInfo.InvariantCulture));
            cells.Add((filter.IncludeVat ? row.GrossPurchasedValue : row.NetPurchasedValue).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.QuantitySold.ToString(CultureInfo.InvariantCulture));
            cells.Add((filter.IncludeVat ? row.GrossSoldValue : row.NetSoldValue).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.Profit.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.QtyPercent.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.ValPercent.ToString("F2", CultureInfo.InvariantCulture));
            if (filter.ShowStock) cells.Add(row.TotalStockQty.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        if (rows.Count > 0)
        {
            if (hasL3) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL3}", l3Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (hasL2) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL2}", l2Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (hasL1) WriteCsvSubtotalRow(sb, $"Subtotal: {prevL1}", l1Agg, filter, hasL1, hasL2, hasL3, hasItem);
        }

        if (totals != null)
        {
            var cells = new List<string>();
            int skip = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
            for (int i = 0; i < skip; i++) cells.Add("");
            if (hasItem) { cells.Add("TOTAL"); cells.Add(""); } else cells.Add("TOTAL");
            cells.Add(totals.TotalQtyPurchased.ToString(CultureInfo.InvariantCulture));
            cells.Add((filter.IncludeVat ? totals.TotalGrossPurchased : totals.TotalNetPurchased).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(totals.TotalQtySold.ToString(CultureInfo.InvariantCulture));
            cells.Add((filter.IncludeVat ? totals.TotalGrossSold : totals.TotalNetSold).ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(totals.TotalProfit.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(totals.QtyPercent.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(totals.ValPercent.ToString("F2", CultureInfo.InvariantCulture));
            if (filter.ShowStock) cells.Add(totals.TotalStockQty.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    private void WriteCsvSubtotalRow(StringBuilder sb, string label, SubtotalAgg agg,
        PurchasesSalesFilter filter, bool hasL1, bool hasL2, bool hasL3, bool hasItem)
    {
        var cells = new List<string>();
        int skip = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
        for (int i = 0; i < skip; i++) cells.Add("");
        if (hasItem) { cells.Add(label); cells.Add(""); } else cells.Add(label);
        cells.Add(agg.QtyPurchased.ToString(CultureInfo.InvariantCulture));
        cells.Add(agg.ValPurchased.ToString("F2", CultureInfo.InvariantCulture));
        cells.Add(agg.QtySold.ToString(CultureInfo.InvariantCulture));
        cells.Add(agg.ValSold.ToString("F2", CultureInfo.InvariantCulture));
        cells.Add(agg.Profit.ToString("F2", CultureInfo.InvariantCulture));
        cells.Add(agg.QtyPct.ToString("F2", CultureInfo.InvariantCulture));
        cells.Add(agg.ValPct.ToString("F2", CultureInfo.InvariantCulture));
        if (filter.ShowStock) cells.Add(agg.StockQty.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join(",", cells.Select(Escape)));
    }

    public byte[] GenerateChartCsv(List<ChartDataPoint> data, ChartFilter filter)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Charts & Dashboards — Data Export");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Dimension: {filter.Dimension}");
        sb.AppendLine($"# Metric: {filter.Metric}");
        sb.AppendLine($"# Top N: {filter.TopN}");
        sb.AppendLine($"# Include VAT: {(filter.IncludeVat ? "Yes" : "No")}");
        if (filter.CompareLastYear) sb.AppendLine("# Compare Last Year: Yes");
        if (filter.ShowOthers) sb.AppendLine("# Show Others: Yes");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        bool hasCompare = filter.CompareLastYear && data.Any(d => d.CompareValue.HasValue);
        var metricLabel = filter.Metric == Core.Models.ChartMetric.Quantity ? "Quantity" : "Value";
        var headers = new List<string> { "#", filter.Dimension.ToString(), metricLabel, "%" };
        if (hasCompare) { headers.Add("Last Year"); headers.Add("YoY %"); }
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        decimal total = data.Sum(d => d.Value);
        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            var pct = total > 0 ? (d.Value / total * 100) : 0;
            var cells = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                d.Label,
                d.Value.ToString("F2", CultureInfo.InvariantCulture),
                pct.ToString("F1", CultureInfo.InvariantCulture)
            };
            if (hasCompare)
            {
                cells.Add((d.CompareValue ?? 0).ToString("F2", CultureInfo.InvariantCulture));
                var yoy = d.CompareValue.HasValue && d.CompareValue.Value > 0
                    ? ((d.Value - d.CompareValue.Value) / d.CompareValue.Value * 100) : 0;
                cells.Add(yoy.ToString("F1", CultureInfo.InvariantCulture));
            }
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        var totalCells = new List<string> { "", "TOTAL",
            total.ToString("F2", CultureInfo.InvariantCulture), "100.0" };
        if (hasCompare) { totalCells.Add(""); totalCells.Add(""); }
        sb.AppendLine(string.Join(",", totalCells.Select(Escape)));

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    public byte[] GenerateParetoCsv(ParetoResult result, ParetoFilter filter)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Pareto 80/20 Analysis");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Dimension: {filter.Dimension}");
        sb.AppendLine($"# Metric: {filter.Metric}");
        sb.AppendLine($"# Include VAT: {(filter.IncludeVat ? "Yes" : "No")}");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Class A Threshold: {filter.ClassAThreshold}%");
        sb.AppendLine($"# Class B Threshold: {filter.ClassBThreshold}%");
        sb.AppendLine($"# Grand Total: {result.GrandTotal.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"# Class A: {result.ClassACount} items | Class B: {result.ClassBCount} items | Class C: {result.ClassCCount} items");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        var metricLabel = filter.Metric == Core.Models.ParetoMetric.Quantity ? "Quantity" : "Value";
        sb.AppendLine(string.Join(",", new[] { "Rank", "Code", "Name", metricLabel, "%", "Cumul. %", "Class" }.Select(Escape)));

        foreach (var row in result.Rows)
        {
            var cells = new List<string>
            {
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.Code,
                row.Name,
                row.Value.ToString("F2", CultureInfo.InvariantCulture),
                row.Percentage.ToString("F2", CultureInfo.InvariantCulture),
                row.CumulativePercentage.ToString("F1", CultureInfo.InvariantCulture),
                row.Classification
            };
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        var totalCells = new List<string> { "", "", "TOTAL",
            result.GrandTotal.ToString("F2", CultureInfo.InvariantCulture), "100.00", "", "" };
        sb.AppendLine(string.Join(",", totalCells.Select(Escape)));

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
