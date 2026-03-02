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

        // Headers
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

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
