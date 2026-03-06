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

        foreach (var row in rows)
        {
            var cells = new List<string>();
            if (hasL1) cells.Add(row.Level1Value ?? row.Level1 ?? "N/A");
            if (hasL2) cells.Add(row.Level2Value ?? row.Level2 ?? "N/A");
            if (hasL3) cells.Add(row.Level3Value ?? row.Level3 ?? "N/A");
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

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
