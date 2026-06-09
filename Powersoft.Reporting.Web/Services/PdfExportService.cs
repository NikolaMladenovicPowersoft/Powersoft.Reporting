using iTextSharp.text;
using iTextSharp.text.pdf;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services;

public class PdfExportService
{
    private static readonly BaseColor HeaderBg = new(37, 99, 235);
    private static readonly BaseColor TotalBg = new(219, 234, 254);
    private static readonly Font TitleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14);
    private static readonly Font SubtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.Gray);
    private static readonly Font HeaderFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.White);
    private static readonly Font CellFont = FontFactory.GetFont(FontFactory.HELVETICA, 7);
    private static readonly Font TotalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7);

    public byte[] GenerateAverageBasketPdf(
        List<AverageBasketRow> rows,
        ReportGrandTotals? grandTotals,
        ReportFilter filter)
    {
        bool hasGrouping = filter.GroupBy != Core.Enums.GroupByType.None;
        bool hasSecondary = filter.SecondaryGroupBy != Core.Enums.GroupByType.None;
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;

        int colCount = 10 + (hasGrouping ? 1 : 0) + (hasSecondary ? 1 : 0) + (compareLY ? 3 : 0);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Average Basket Report", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}", SubtitleFont));
        document.Add(new Paragraph($"Breakdown: {filter.Breakdown} | Group By: {filter.GroupBy}" +
            (filter.SecondaryGroupBy != Core.Enums.GroupByType.None ? $" | Secondary: {filter.SecondaryGroupBy}" : ""), SubtitleFont));
        document.Add(new Paragraph($"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}" +
            (filter.CompareLastYear ? " | Compare Last Year: Yes" : ""), SubtitleFont));
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            document.Add(new Paragraph($"Stores: {string.Join(", ", filter.StoreCodes)}", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount)
        {
            WidthPercentage = 100,
            SpacingBefore = 5f
        };

        // Column widths
        var widths = BuildColumnWidths(hasGrouping, hasSecondary, compareLY, colCount);
        table.SetWidths(widths);

        // Headers
        if (hasGrouping) AddHeaderCell(table, filter.GroupBy.ToString());
        if (hasSecondary) AddHeaderCell(table, filter.SecondaryGroupBy.ToString());
        AddHeaderCell(table, "Period");
        AddHeaderCell(table, "Invoices");
        AddHeaderCell(table, "Returns");
        AddHeaderCell(table, "Net Trans.");
        AddHeaderCell(table, "Qty Sold");
        AddHeaderCell(table, "Qty Ret.");
        AddHeaderCell(table, "Net Qty");
        AddHeaderCell(table, includeVat ? "Gross Sales" : "Net Sales");
        AddHeaderCell(table, "Avg Basket");
        AddHeaderCell(table, "Avg Qty");
        if (compareLY)
        {
            AddHeaderCell(table, "LY Sales");
            AddHeaderCell(table, "LY Avg");
            AddHeaderCell(table, "YoY %");
        }

        // Data rows
        bool alternate = false;
        void WriteRow(AverageBasketRow row)
        {
            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            if (hasGrouping) AddDataCell(table, row.Level1Value ?? row.Level1 ?? "N/A", bg);
            if (hasSecondary) AddDataCell(table, row.Level2Value ?? row.Level2 ?? "N/A", bg);
            AddDataCell(table, row.Period, bg);
            AddDataCell(table, row.CYInvoiceCount.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYCreditCount.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYTotalTransactions.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYQtySold.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYQtyReturned.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYTotalQty.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, (includeVat ? row.CYTotalGross : row.CYTotalNet).ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, (includeVat ? row.CYAverageGross : row.CYAverageNet).ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.CYAverageQty.ToString("N2"), bg, Element.ALIGN_RIGHT);
            if (compareLY)
            {
                AddDataCell(table, (includeVat ? row.LYTotalGross : row.LYTotalNet).ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, (includeVat ? row.LYAverageGross : row.LYAverageNet).ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, $"{row.YoYChangePercent:N1}%", bg, Element.ALIGN_RIGHT);
            }
            alternate = !alternate;
        }

        // Subtotal: counts/qty/sales summed; Avg Basket / Avg Qty recomputed from sums
        // (Sales / Net Trans.) exactly like the on-screen grid.
        var subBg = new BaseColor(238, 242, 255);
        var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.DarkGray);
        void WriteSub(List<AverageBasketRow> g)
        {
            int leading = (hasGrouping ? 1 : 0) + (hasSecondary ? 1 : 0) + 1; // groups + Period
            AddSubtotalLabelCell(table, "Subtotal", leading, subBg, subFont);
            var gTrans = g.Sum(r => r.CYTotalTransactions);
            var gNetQty = g.Sum(r => r.CYTotalQty);
            var gSales = g.Sum(r => includeVat ? r.CYTotalGross : r.CYTotalNet);
            var gAvgBasket = gTrans > 0 ? gSales / gTrans : 0m;
            var gAvgQty = gTrans > 0 ? gNetQty / gTrans : 0m;
            AddSubtotalCell(table, g.Sum(r => r.CYInvoiceCount).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, g.Sum(r => r.CYCreditCount).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, gTrans.ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, g.Sum(r => r.CYQtySold).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, g.Sum(r => r.CYQtyReturned).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, gNetQty.ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, gSales.ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, gAvgBasket.ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, gAvgQty.ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            if (compareLY)
            {
                var gLyTrans = g.Sum(r => r.LYTotalTransactions);
                var gLySales = g.Sum(r => includeVat ? r.LYTotalGross : r.LYTotalNet);
                var gLyAvg = gLyTrans > 0 ? gLySales / gLyTrans : 0m;
                var gYoY = gLySales != 0 ? Math.Round((gSales - gLySales) / Math.Abs(gLySales) * 100, 2) : (gSales > 0 ? 100m : 0m);
                AddSubtotalCell(table, gLySales.ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, gLyAvg.ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, $"{gYoY:N1}%", subBg, subFont, Element.ALIGN_RIGHT);
            }
        }

        if (hasGrouping)
        {
            foreach (var grp in rows.GroupBy(r => r.Level1Value ?? r.Level1 ?? "N/A"))
            {
                var gr = grp.ToList();
                foreach (var row in gr) WriteRow(row);
                WriteSub(gr);
            }
        }
        else
        {
            foreach (var row in rows) WriteRow(row);
        }

        // Grand total
        if (grandTotals != null)
        {
            if (hasGrouping) AddTotalCell(table, "");
            if (hasSecondary) AddTotalCell(table, "");
            AddTotalCell(table, "GRAND TOTAL");
            AddTotalCell(table, grandTotals.TotalInvoices.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.TotalCredits.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.NetTransactions.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.TotalQtySold.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.TotalQtyReturned.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.NetQty.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, (includeVat ? grandTotals.GrossSales : grandTotals.NetSales).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, (includeVat ? grandTotals.AverageBasketGross : grandTotals.AverageBasketNet).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, grandTotals.AverageQty.ToString("N2"), Element.ALIGN_RIGHT);
            
            if (compareLY)
            {
                AddTotalCell(table, (includeVat ? grandTotals.LYTotalGross : grandTotals.LYTotalNet).ToString("N2"), Element.ALIGN_RIGHT);
                AddTotalCell(table, (includeVat ? grandTotals.LYAverageBasketGross : grandTotals.LYAverageBasketNet).ToString("N2"), Element.ALIGN_RIGHT);
                AddTotalCell(table, $"{grandTotals.YoYChangePercent:N1}%", Element.ALIGN_RIGHT);
            }
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    private float[] BuildColumnWidths(bool hasGrouping, bool hasSecondary, bool compareLY, int colCount)
    {
        var widths = new List<float>();
        if (hasGrouping) widths.Add(12f);
        if (hasSecondary) widths.Add(12f);
        widths.AddRange(new[] { 10f, 6f, 6f, 6f, 6f, 6f, 6f, 9f, 8f, 7f });
        if (compareLY) widths.AddRange(new[] { 9f, 8f, 6f });
        return widths.ToArray();
    }

    private static void AddHeaderCell(PdfPTable table, string text)
    {
        var cell = new PdfPCell(new Phrase(text, HeaderFont))
        {
            BackgroundColor = HeaderBg,
            HorizontalAlignment = Element.ALIGN_CENTER,
            Padding = 4f
        };
        table.AddCell(cell);
    }

    private static void AddDataCell(PdfPTable table, string text, BaseColor bg, int align = Element.ALIGN_LEFT)
    {
        var cell = new PdfPCell(new Phrase(text, CellFont))
        {
            BackgroundColor = bg,
            HorizontalAlignment = align,
            Padding = 3f,
            BorderColor = new BaseColor(226, 232, 240)
        };
        table.AddCell(cell);
    }

    private static void AddTotalCell(PdfPTable table, string text, int align = Element.ALIGN_LEFT)
    {
        var cell = new PdfPCell(new Phrase(text, TotalFont))
        {
            BackgroundColor = TotalBg,
            HorizontalAlignment = align,
            Padding = 4f,
            BorderWidthTop = 1.5f
        };
        table.AddCell(cell);
    }

    public byte[] GeneratePurchasesSalesPdf(
        List<PurchasesSalesRow> rows,
        PurchasesSalesTotals? totals,
        PurchasesSalesFilter filter)
    {
        bool hasL1 = filter.PrimaryGroup != Core.Enums.PsGroupBy.None;
        bool hasL2 = filter.SecondaryGroup != Core.Enums.PsGroupBy.None;
        bool hasL3 = filter.ThirdGroup != Core.Enums.PsGroupBy.None;
        bool hasItem = !filter.IsSummary || (!hasL1 && !hasL2 && !hasL3);

        int colCount = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0)
                     + (hasItem ? 2 : 0) + 7 + (filter.ShowStock ? 1 : 0)
                     + (filter.ShowOnOrder ? 1 : 0) + (filter.ShowReservation ? 1 : 0)
                     + (filter.ShowAvailable ? 1 : 0);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Purchases vs Sales Report", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}", SubtitleFont));
        document.Add(new Paragraph($"Mode: {filter.ReportMode}" +
            (hasL1 ? $" | Primary: {filter.PrimaryGroup}" : "") +
            (hasL2 ? $" | Secondary: {filter.SecondaryGroup}" : "") +
            (hasL3 ? $" | Third: {filter.ThirdGroup}" : ""), SubtitleFont));
        document.Add(new Paragraph($"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}" +
            (filter.ShowProfit ? " | Profit: Yes" : "") +
            (filter.ShowStock ? " | Stock: Yes" : "") +
            (filter.ShowOnOrder ? " | On Order: Yes" : "") +
            (filter.ShowReservation ? " | Reserved: Yes" : "") +
            (filter.ShowAvailable ? " | Available: Yes" : "") +
            (!filter.IncludeAdditionalCharges ? " | Cost: Wholesale only" : ""), SubtitleFont));
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            document.Add(new Paragraph($"Stores: {string.Join(", ", filter.StoreCodes)}", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 5f };

        if (hasL1) AddHeaderCell(table, filter.PrimaryGroup.ToString());
        if (hasL2) AddHeaderCell(table, filter.SecondaryGroup.ToString());
        if (hasL3) AddHeaderCell(table, filter.ThirdGroup.ToString());
        if (hasItem) { AddHeaderCell(table, "Code"); AddHeaderCell(table, "Name"); }
        AddHeaderCell(table, "Qty Purch");
        AddHeaderCell(table, filter.IncludeVat ? "Gross Purch" : "Net Purch");
        AddHeaderCell(table, "Qty Sold");
        AddHeaderCell(table, filter.IncludeVat ? "Gross Sold" : "Net Sold");
        AddHeaderCell(table, "Profit");
        AddHeaderCell(table, "Qty %");
        AddHeaderCell(table, "Val %");
        if (filter.ShowStock) AddHeaderCell(table, "Stock");
        if (filter.ShowOnOrder) AddHeaderCell(table, "On Order");
        if (filter.ShowReservation) AddHeaderCell(table, "Reserved");
        if (filter.ShowAvailable) AddHeaderCell(table, "Available");

        bool alternate = false;
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

            if (l3Changed) WritePdfSubtotalRow(table, $"Subtotal: {prevL3}", l3Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);
            if (l2Changed) WritePdfSubtotalRow(table, $"Subtotal: {prevL2}", l2Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);
            if (l1Changed) WritePdfSubtotalRow(table, $"Subtotal: {prevL1}", l1Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);

            if (l1Changed || ri == 0) { l1Agg = new SubtotalAgg(); l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l2Changed) { l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l3Changed) { l3Agg = new SubtotalAgg(); }
            prevL1 = curL1; prevL2 = curL2; prevL3 = curL3;

            l1Agg.Add(row, filter.IncludeVat); l2Agg.Add(row, filter.IncludeVat); l3Agg.Add(row, filter.IncludeVat);

            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            if (hasL1) AddDataCell(table, curL1, bg);
            if (hasL2) AddDataCell(table, curL2, bg);
            if (hasL3) AddDataCell(table, curL3, bg);
            if (hasItem) { AddDataCell(table, row.ItemCode ?? "", bg); AddDataCell(table, row.ItemName ?? "", bg); }
            AddDataCell(table, row.QuantityPurchased.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, (filter.IncludeVat ? row.GrossPurchasedValue : row.NetPurchasedValue).ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.QuantitySold.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, (filter.IncludeVat ? row.GrossSoldValue : row.NetSoldValue).ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.Profit.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, $"{row.QtyPercent:N1}%", bg, Element.ALIGN_RIGHT);
            AddDataCell(table, $"{row.ValPercent:N1}%", bg, Element.ALIGN_RIGHT);
            if (filter.ShowStock) AddDataCell(table, row.TotalStockQty.ToString("N0"), bg, Element.ALIGN_RIGHT);
            if (filter.ShowOnOrder) AddDataCell(table, row.QtyOnOrder.ToString("N0"), bg, Element.ALIGN_RIGHT);
            if (filter.ShowReservation) AddDataCell(table, row.QtyReserved.ToString("N0"), bg, Element.ALIGN_RIGHT);
            if (filter.ShowAvailable) AddDataCell(table, row.QtyAvailable.ToString("N0"), bg, Element.ALIGN_RIGHT);
            alternate = !alternate;
        }

        if (rows.Count > 0)
        {
            if (hasL3) WritePdfSubtotalRow(table, $"Subtotal: {prevL3}", l3Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);
            if (hasL2) WritePdfSubtotalRow(table, $"Subtotal: {prevL2}", l2Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);
            if (hasL1) WritePdfSubtotalRow(table, $"Subtotal: {prevL1}", l1Agg, filter, colCount, hasL1, hasL2, hasL3, hasItem);
        }

        if (totals != null)
        {
            int skip = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
            for (int i = 0; i < skip; i++) AddTotalCell(table, "");
            if (hasItem) { AddTotalCell(table, "TOTAL"); AddTotalCell(table, ""); }
            AddTotalCell(table, totals.TotalQtyPurchased.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, (filter.IncludeVat ? totals.TotalGrossPurchased : totals.TotalNetPurchased).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, totals.TotalQtySold.ToString("N0"), Element.ALIGN_RIGHT);
            AddTotalCell(table, (filter.IncludeVat ? totals.TotalGrossSold : totals.TotalNetSold).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, totals.TotalProfit.ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, $"{totals.QtyPercent:N1}%", Element.ALIGN_RIGHT);
            AddTotalCell(table, $"{totals.ValPercent:N1}%", Element.ALIGN_RIGHT);
            if (filter.ShowStock) AddTotalCell(table, totals.TotalStockQty.ToString("N0"), Element.ALIGN_RIGHT);
            if (filter.ShowOnOrder) AddTotalCell(table, totals.TotalQtyOnOrder.ToString("N0"), Element.ALIGN_RIGHT);
            if (filter.ShowReservation) AddTotalCell(table, totals.TotalQtyReserved.ToString("N0"), Element.ALIGN_RIGHT);
            if (filter.ShowAvailable) AddTotalCell(table, totals.TotalQtyAvailable.ToString("N0"), Element.ALIGN_RIGHT);
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    public byte[] GenerateParetoPdf(ParetoResult result, ParetoFilter filter, bool viewCost = true)
    {
        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4, 30, 30, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Pareto 80/20 Analysis", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}", SubtitleFont));
        document.Add(new Paragraph($"Dimension: {filter.Dimension} | Metric: {filter.Metric}", SubtitleFont));
        document.Add(new Paragraph($"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}", SubtitleFont));
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            document.Add(new Paragraph($"Stores: {string.Join(", ", filter.StoreCodes)}", SubtitleFont));
        document.Add(new Paragraph($"Thresholds: A ≤ {filter.ClassAThreshold}% | B ≤ {filter.ClassBThreshold}%", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var summaryFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
        var summaryBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8);
        decimal aPct = result.GrandTotal > 0 ? result.ClassAValue / result.GrandTotal * 100 : 0;
        decimal bPct = result.GrandTotal > 0 ? result.ClassBValue / result.GrandTotal * 100 : 0;
        decimal cPct = result.GrandTotal > 0 ? result.ClassCValue / result.GrandTotal * 100 : 0;

        var summaryTable = new PdfPTable(4) { WidthPercentage = 100, SpacingBefore = 2f, SpacingAfter = 8f };
        summaryTable.SetWidths(new float[] { 25f, 25f, 25f, 25f });

        AddSummaryCard(summaryTable, $"Total: {result.GrandTotal:N2}", $"{result.Rows.Count} items", new BaseColor(219, 234, 254), summaryBold, summaryFont);
        AddSummaryCard(summaryTable, $"Class A: {result.ClassACount}", $"{aPct:N1}% of value", new BaseColor(220, 252, 231), summaryBold, summaryFont);
        AddSummaryCard(summaryTable, $"Class B: {result.ClassBCount}", $"{bPct:N1}% of value", new BaseColor(254, 249, 195), summaryBold, summaryFont);
        AddSummaryCard(summaryTable, $"Class C: {result.ClassCCount}", $"{cPct:N1}% of value", new BaseColor(254, 226, 226), summaryBold, summaryFont);
        document.Add(summaryTable);

        var table = new PdfPTable(viewCost ? 9 : 8) { WidthPercentage = 100, SpacingBefore = 5f };
        table.SetWidths(viewCost
            ? new float[] { 4f, 10f, 22f, 10f, 12f, 12f, 8f, 9f, 6f }
            : new float[] { 4f, 10f, 24f, 12f, 14f, 9f, 11f, 6f });

        AddHeaderCell(table, "#");
        AddHeaderCell(table, "Code");
        AddHeaderCell(table, "Name");
        AddHeaderCell(table, "Qty");
        AddHeaderCell(table, "Subtotal");
        if (viewCost) AddHeaderCell(table, "Profit");
        AddHeaderCell(table, "%");
        AddHeaderCell(table, "Cumul. %");
        AddHeaderCell(table, "Class");

        foreach (var row in result.Rows)
        {
            var bg = row.Classification switch
            {
                "A" => new BaseColor(220, 252, 231),
                "B" => new BaseColor(254, 249, 195),
                _ => new BaseColor(254, 226, 226)
            };

            AddDataCell(table, row.Rank.ToString(), bg, Element.ALIGN_CENTER);
            AddDataCell(table, row.Code, bg);
            AddDataCell(table, row.Name, bg);
            AddDataCell(table, row.Quantity.ToString("N0"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.Subtotal.ToString("N2"), bg, Element.ALIGN_RIGHT);
            if (viewCost) AddDataCell(table, row.Profit.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, $"{row.Percentage:N2}%", bg, Element.ALIGN_RIGHT);
            AddDataCell(table, $"{row.CumulativePercentage:N1}%", bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.Classification, bg, Element.ALIGN_CENTER);
        }

        AddTotalCell(table, "");
        AddTotalCell(table, "");
        AddTotalCell(table, "TOTAL");
        AddTotalCell(table, result.TotalQuantity.ToString("N0"), Element.ALIGN_RIGHT);
        AddTotalCell(table, result.TotalSubtotal.ToString("N2"), Element.ALIGN_RIGHT);
        if (viewCost) AddTotalCell(table, result.TotalProfit.ToString("N2"), Element.ALIGN_RIGHT);
        AddTotalCell(table, "100%", Element.ALIGN_RIGHT);
        AddTotalCell(table, "");
        AddTotalCell(table, "");

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    public byte[] GenerateChartPdf(List<ChartDataPoint> data, ChartFilter filter)
    {
        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4, 30, 30, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Charts & Dashboards — Data Export", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}", SubtitleFont));
        document.Add(new Paragraph($"Mode: {filter.Mode} | Dimension: {filter.Dimension} | Metric: {filter.Metric} | Top {filter.TopN}", SubtitleFont));
        document.Add(new Paragraph($"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}" +
            (filter.CompareLastYear ? " | Compare Last Year: Yes" : ""), SubtitleFont));
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            document.Add(new Paragraph($"Stores: {string.Join(", ", filter.StoreCodes)}", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        bool hasCompare = filter.CompareLastYear && data.Any(d => d.CompareValue.HasValue);
        bool hasValue2 = data.Any(d => d.Value2.HasValue);

        int colCount = 3;
        if (hasValue2) colCount += 2;
        if (hasCompare && !hasValue2) colCount += 2;

        var table = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 5f };

        AddHeaderCell(table, "#");
        AddHeaderCell(table, filter.Dimension.ToString());
        string valLabel = filter.Metric == ChartMetric.Quantity ? "Qty" : filter.Metric == ChartMetric.Count ? "Transactions" : "Value";
        AddHeaderCell(table, valLabel);

        if (hasValue2)
        {
            AddHeaderCell(table, "Value 2");
            AddHeaderCell(table, "Difference");
        }
        else if (hasCompare)
        {
            AddHeaderCell(table, "Last Year");
            AddHeaderCell(table, "YoY %");
        }

        decimal grandTotal = data.Sum(d => d.Value);
        bool alternate = false;
        for (int i = 0; i < data.Count; i++)
        {
            var row = data[i];
            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            bool isValueMetric = filter.Metric == ChartMetric.Value;
            string fmtVal = isValueMetric ? row.Value.ToString("N2") : row.Value.ToString("N0");

            AddDataCell(table, (i + 1).ToString(), bg, Element.ALIGN_CENTER);
            AddDataCell(table, row.Label, bg);
            AddDataCell(table, fmtVal, bg, Element.ALIGN_RIGHT);

            if (hasValue2)
            {
                AddDataCell(table, isValueMetric ? (row.Value2 ?? 0).ToString("N2") : (row.Value2 ?? 0).ToString("N0"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, isValueMetric ? (row.DiffValue ?? 0).ToString("N2") : (row.DiffValue ?? 0).ToString("N0"), bg, Element.ALIGN_RIGHT);
            }
            else if (hasCompare)
            {
                AddDataCell(table, isValueMetric ? (row.CompareValue ?? 0).ToString("N2") : (row.CompareValue ?? 0).ToString("N0"), bg, Element.ALIGN_RIGHT);
                var yoy = row.CompareValue.HasValue && row.CompareValue != 0
                    ? $"{(row.Value - row.CompareValue.Value) / row.CompareValue.Value * 100:N1}%"
                    : "N/A";
                AddDataCell(table, yoy, bg, Element.ALIGN_RIGHT);
            }

            alternate = !alternate;
        }

        AddTotalCell(table, "");
        AddTotalCell(table, "TOTAL");
        AddTotalCell(table, filter.Metric == ChartMetric.Value ? grandTotal.ToString("N2") : grandTotal.ToString("N0"), Element.ALIGN_RIGHT);
        if (hasValue2)
        {
            AddTotalCell(table, data.Sum(d => d.Value2 ?? 0).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, data.Sum(d => d.DiffValue ?? 0).ToString("N2"), Element.ALIGN_RIGHT);
        }
        else if (hasCompare)
        {
            AddTotalCell(table, data.Sum(d => d.CompareValue ?? 0).ToString("N2"), Element.ALIGN_RIGHT);
            AddTotalCell(table, "");
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    private static void AddSummaryCard(PdfPTable table, string title, string subtitle, BaseColor bg, Font titleFont, Font subFont)
    {
        var phrase = new Phrase();
        phrase.Add(new Chunk(title + "\n", titleFont));
        phrase.Add(new Chunk(subtitle, subFont));
        var cell = new PdfPCell(phrase)
        {
            BackgroundColor = bg,
            Padding = 8f,
            HorizontalAlignment = Element.ALIGN_CENTER,
            BorderColor = new BaseColor(226, 232, 240)
        };
        table.AddCell(cell);
    }

    public byte[] GenerateCancelLogPdf(
        List<CancelLogDetailedRow>? detailedRows,
        List<CancelLogSummaryRow>? summaryRows,
        CancelLogFilter filter)
    {
        bool isDetailed = filter.ReportType == CancelLogReportType.Detailed;
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";

        int colCount;
        if (isDetailed)
            colCount = 17 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);
        else
            colCount = 7 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);

        var subBg = new BaseColor(241, 245, 249);
        var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.DarkGray);
        int g = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);

        using var ms = new MemoryStream();
        var pageSize = isDetailed ? PageSize.A4.Rotate() : PageSize.A4;
        var document = new Document(pageSize, 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Cancellation Logging Report", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}", SubtitleFont));
        document.Add(new Paragraph($"Report Type: {filter.ReportType} | Action Type: {filter.ActionType}" +
            (hasL1 ? $" | Primary: {filter.PrimaryGroup}" : "") +
            (hasL2 ? $" | Secondary: {filter.SecondaryGroup}" : ""), SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 5f };

        if (isDetailed)
        {
            var widths = new List<float> { 8f };
            if (hasL1) widths.Add(7f);
            if (hasL2) widths.Add(7f);
            widths.AddRange(new[] { 5f, 7f, 4f, 7f, 5f, 8f, 4f, 5f, 4f, 3f, 5f, 4f, 5f, 4f, 5f, 5f });
            table.SetWidths(widths.ToArray());

            AddHeaderCell(table, "Store/Stn");
            if (hasL1) AddHeaderCell(table, "Group 1");
            if (hasL2) AddHeaderCell(table, "Group 2");
            AddHeaderCell(table, "Action");
            AddHeaderCell(table, "Session");
            AddHeaderCell(table, "Kind");
            AddHeaderCell(table, "Customer");
            AddHeaderCell(table, "Code");
            AddHeaderCell(table, "Item");
            AddHeaderCell(table, "User");
            AddHeaderCell(table, "Inv/Crd");
            AddHeaderCell(table, "Z Rep");
            AddHeaderCell(table, "Lines");
            AddHeaderCell(table, "Inv Total");
            AddHeaderCell(table, "Qty");
            AddHeaderCell(table, "Amount");
            AddHeaderCell(table, "Tbl No");
            AddHeaderCell(table, "Table");
            AddHeaderCell(table, "Compart.");

            bool alternate = false;
            void WriteRow(CancelLogDetailedRow row)
            {
                var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
                AddDataCell(table, row.StoreAndStation, bg);
                if (hasL1) AddDataCell(table, row.Level1Descr, bg);
                if (hasL2) AddDataCell(table, row.Level2Descr, bg);
                AddDataCell(table, row.ActionType, bg);
                AddDataCell(table, row.SessionDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "", bg);
                AddDataCell(table, row.TransKind == "I" ? "Sale" : "Return", bg);
                AddDataCell(table, row.CustomerFullName, bg);
                AddDataCell(table, row.ItemCode, bg);
                AddDataCell(table, row.ItemDescr, bg);
                AddDataCell(table, row.UserCode, bg);
                AddDataCell(table, !string.IsNullOrEmpty(row.InvoiceId) ? row.InvoiceId : row.CreditId, bg);
                AddDataCell(table, row.ZReport, bg);
                AddDataCell(table, row.TotalInvoiceLines.ToString("N0"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.InvoiceTotal.ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.Quantity.ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.Amount.ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.TableNo, bg);
                AddDataCell(table, row.TableName, bg);
                AddDataCell(table, row.CompartmentName, bg);
                alternate = !alternate;
            }

            void WriteSub(List<CancelLogDetailedRow> grp, string label)
            {
                // Label spans Store/Stn..Lines (11 + group cols); then Inv Total, Qty, Amount;
                // then 3 trailing blanks (Tbl No, Table, Compart.).
                AddSubtotalLabelCell(table, label, 11 + g, subBg, subFont);
                AddSubtotalCell(table, grp.Sum(x => x.InvoiceTotal).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.Quantity).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.Amount).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, "", subBg, subFont);
                AddSubtotalCell(table, "", subBg, subFont);
                AddSubtotalCell(table, "", subBg, subFont);
            }

            var det = detailedRows ?? new();
            if (hasL1)
            {
                foreach (var l1 in det.GroupBy(r => r.Level1Descr ?? r.Level1Code ?? "N/A"))
                {
                    var l1Rows = l1.ToList();
                    if (hasL2)
                    {
                        foreach (var l2 in l1Rows.GroupBy(r => r.Level2Descr ?? r.Level2Code ?? "N/A"))
                        {
                            var l2Rows = l2.ToList();
                            foreach (var row in l2Rows) WriteRow(row);
                            WriteSub(l2Rows, l2.Key + " subtotal");
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteRow(row);
                    }
                    WriteSub(l1Rows, l1.Key + " total");
                }
            }
            else
            {
                foreach (var row in det) WriteRow(row);
            }
        }
        else
        {
            var widths = new List<float> { 20f };
            if (hasL1) widths.Add(15f);
            if (hasL2) widths.Add(15f);
            widths.AddRange(new[] { 10f, 10f, 12f, 12f, 10f, 12f });
            table.SetWidths(widths.ToArray());

            AddHeaderCell(table, "Store/Station");
            if (hasL1) AddHeaderCell(table, "Group 1");
            if (hasL2) AddHeaderCell(table, "Group 2");
            AddHeaderCell(table, "Deleted");
            AddHeaderCell(table, "Cancelled");
            AddHeaderCell(table, "Complimentary");
            AddHeaderCell(table, "Invoice Total");
            AddHeaderCell(table, "Quantity");
            AddHeaderCell(table, "Amount");

            bool alternate = false;
            void WriteRow(CancelLogSummaryRow row)
            {
                var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
                AddDataCell(table, row.StoreAndStation, bg);
                if (hasL1) AddDataCell(table, row.Level1Descr, bg);
                if (hasL2) AddDataCell(table, row.Level2Descr, bg);
                AddDataCell(table, row.DeletedAction.ToString("N0"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.CancelledAction.ToString("N0"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.ComplimentaryAction.ToString("N0"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.InvoiceTotal.ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.Quantity.ToString("N2"), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, row.Amount.ToString("N2"), bg, Element.ALIGN_RIGHT);
                alternate = !alternate;
            }

            void WriteSub(List<CancelLogSummaryRow> grp, string label)
            {
                // Label spans Store/Station + group cols; then the 6 metric columns.
                AddSubtotalLabelCell(table, label, 1 + g, subBg, subFont);
                AddSubtotalCell(table, grp.Sum(x => x.DeletedAction).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.CancelledAction).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.ComplimentaryAction).ToString("N0"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.InvoiceTotal).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.Quantity).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
                AddSubtotalCell(table, grp.Sum(x => x.Amount).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            }

            var sum = summaryRows ?? new();
            if (hasL1)
            {
                foreach (var l1 in sum.GroupBy(r => r.Level1Descr ?? r.Level1Code ?? "N/A"))
                {
                    var l1Rows = l1.ToList();
                    if (hasL2)
                    {
                        foreach (var l2 in l1Rows.GroupBy(r => r.Level2Descr ?? r.Level2Code ?? "N/A"))
                        {
                            var l2Rows = l2.ToList();
                            foreach (var row in l2Rows) WriteRow(row);
                            WriteSub(l2Rows, l2.Key + " subtotal");
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteRow(row);
                    }
                    WriteSub(l1Rows, l1.Key + " total");
                }
            }
            else
            {
                foreach (var row in sum) WriteRow(row);
            }
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    private void WritePdfSubtotalRow(PdfPTable table, string label, SubtotalAgg agg,
        PurchasesSalesFilter filter, int colCount,
        bool hasL1, bool hasL2, bool hasL3, bool hasItem)
    {
        var subtotalBg = new BaseColor(241, 245, 249);
        var subtotalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.DarkGray);
        int skip = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
        for (int i = 0; i < skip; i++) AddSubtotalCell(table, "", subtotalBg, subtotalFont);
        if (hasItem) { AddSubtotalCell(table, label, subtotalBg, subtotalFont); AddSubtotalCell(table, "", subtotalBg, subtotalFont); }
        else AddSubtotalCell(table, label, subtotalBg, subtotalFont);
        AddSubtotalCell(table, agg.QtyPurchased.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, agg.ValPurchased.ToString("N2"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, agg.QtySold.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, agg.ValSold.ToString("N2"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, agg.Profit.ToString("N2"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, $"{agg.QtyPct:N1}%", subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        AddSubtotalCell(table, $"{agg.ValPct:N1}%", subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        if (filter.ShowStock) AddSubtotalCell(table, agg.StockQty.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        if (filter.ShowOnOrder) AddSubtotalCell(table, agg.OnOrderQty.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        if (filter.ShowReservation) AddSubtotalCell(table, agg.ReservedQty.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
        if (filter.ShowAvailable) AddSubtotalCell(table, agg.AvailableQty.ToString("N0"), subtotalBg, subtotalFont, Element.ALIGN_RIGHT);
    }

    private void AddSubtotalCell(PdfPTable table, string text, BaseColor bg, Font font, int align = Element.ALIGN_LEFT)
    {
        var cell = new PdfPCell(new Phrase(text, font))
        {
            BackgroundColor = bg,
            HorizontalAlignment = align,
            Padding = 3f,
            BorderWidth = 0.5f,
            BorderColor = new BaseColor(200, 200, 200)
        };
        table.AddCell(cell);
    }

    private void AddSubtotalLabelCell(PdfPTable table, string text, int colspan, BaseColor bg, Font font)
    {
        var cell = new PdfPCell(new Phrase(text, font))
        {
            BackgroundColor = bg,
            Colspan = colspan,
            HorizontalAlignment = Element.ALIGN_LEFT,
            Padding = 3f,
            BorderWidth = 0.5f,
            BorderColor = new BaseColor(200, 200, 200)
        };
        table.AddCell(cell);
    }

    public byte[] GenerateProspectClientsPdf(
        List<ProspectClientsRow> rows, ProspectClientsFilter filter)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";
        int colCount = 22 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (filter.IncludeHistory ? 1 : 0);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Prospect Clients Report", TitleFont));
        document.Add(new Paragraph(
            $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd} | Date Field: {filter.DateField}" +
            (filter.StatusFilter != "All" ? $" | Status: {filter.StatusFilter}" : "") +
            (filter.PriorityFilter != "All" ? $" | Priority: {filter.PriorityFilter}" : ""),
            SubtitleFont));
        document.Add(new Paragraph($"Total Records: {rows.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount)
        {
            WidthPercentage = 100,
            SpacingBefore = 10
        };

        var headers = new List<string>();
        if (hasL1) headers.Add("Group 1");
        if (hasL2) headers.Add("Group 2");
        headers.AddRange(new[] { "Lead No", "Company/Name", "Contact", "Status", "Priority",
            "Reg. Date", "Last Modified", "Next Comm.", "Phone", "Mobile", "Email", "Town",
            "Followed By", "Recommended By", "Customer",
            "Cat 1", "Cat 2", "Notes",
            "Offers", "Offer Value", "Emails", "SMS" });
        if (filter.IncludeHistory) headers.Add("Source");

        foreach (var h in headers)
        {
            var cell = new PdfPCell(new Phrase(h, HeaderFont))
            {
                BackgroundColor = HeaderBg,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 4f
            };
            table.AddCell(cell);
        }

        var subBg = new BaseColor(241, 245, 249);
        var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.DarkGray);
        int g = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);

        bool alternate = false;
        void WriteRow(ProspectClientsRow row)
        {
            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            if (hasL1) AddDataCell(table, row.Level1Descr, bg);
            if (hasL2) AddDataCell(table, row.Level2Descr, bg);
            AddDataCell(table, row.LeadNo, bg);
            AddDataCell(table, row.CompanyName, bg);
            AddDataCell(table, row.ContactPerson, bg);
            AddDataCell(table, row.StatusName, bg);
            AddDataCell(table, row.PriorityName, bg);
            AddDataCell(table, row.RegistrationDate?.ToString("yyyy-MM-dd") ?? "", bg);
            AddDataCell(table, row.LastModification?.ToString("yyyy-MM-dd") ?? "", bg);
            AddDataCell(table, row.NextCommunicationDate?.ToString("yyyy-MM-dd") ?? "", bg);
            AddDataCell(table, row.Tel1, bg);
            AddDataCell(table, row.Mobile, bg);
            AddDataCell(table, row.Email, bg);
            AddDataCell(table, row.Town, bg);
            AddDataCell(table, row.FollowedBy, bg);
            AddDataCell(table, row.RecommendedBy, bg);
            AddDataCell(table, row.LinkedCustomer, bg);
            AddDataCell(table, row.Category1, bg);
            AddDataCell(table, row.Category2, bg);
            AddDataCell(table, row.Notes, bg);
            AddDataCell(table, row.OfferCount.ToString(), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.TotalOfferValue.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.EmailsSent.ToString(), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.SmsSent.ToString(), bg, Element.ALIGN_RIGHT);
            if (filter.IncludeHistory) AddDataCell(table, row.Source, bg);
            alternate = !alternate;
        }

        void WriteSub(List<ProspectClientsRow> grp, string label)
        {
            // Label spans group cols + Lead No..Notes (18); then Offers, Offer Value, Emails, SMS;
            // then optional Source blank.
            AddSubtotalLabelCell(table, label, g + 18, subBg, subFont);
            AddSubtotalCell(table, grp.Sum(x => x.OfferCount).ToString(), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.TotalOfferValue).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.EmailsSent).ToString(), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.SmsSent).ToString(), subBg, subFont, Element.ALIGN_RIGHT);
            if (filter.IncludeHistory) AddSubtotalCell(table, "", subBg, subFont);
        }

        if (hasL1)
        {
            foreach (var l1 in rows.GroupBy(r => r.Level1Descr ?? r.Level1Code ?? "N/A"))
            {
                var l1Rows = l1.ToList();
                if (hasL2)
                {
                    foreach (var l2 in l1Rows.GroupBy(r => r.Level2Descr ?? r.Level2Code ?? "N/A"))
                    {
                        var l2Rows = l2.ToList();
                        foreach (var row in l2Rows) WriteRow(row);
                        WriteSub(l2Rows, l2.Key + " subtotal");
                    }
                }
                else
                {
                    foreach (var row in l1Rows) WriteRow(row);
                }
                WriteSub(l1Rows, l1.Key + " total");
            }
        }
        else
        {
            foreach (var row in rows) WriteRow(row);
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    public byte[] GenerateOffersReportPdf(
        List<OffersReportRow> rows, OffersReportFilter filter, bool viewCost = true)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";
        bool hasL3 = filter.ThirdGroup != "NONE";
        int g = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
        int colCount = 22 + g - (viewCost ? 0 : 1);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Offers Report", TitleFont));
        document.Add(new Paragraph(
            $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd} | Date Field: {filter.DateField}" +
            (filter.StatusFilter != "All" ? $" | Status: {filter.StatusFilter}" : "") +
            (filter.StoreFilter != "All" ? $" | Store: {filter.StoreFilter}" : ""),
            SubtitleFont));
        document.Add(new Paragraph($"Total Records: {rows.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount)
        {
            WidthPercentage = 100,
            SpacingBefore = 10
        };

        var headers = new List<string>();
        if (hasL1) headers.Add("Group 1");
        if (hasL2) headers.Add("Group 2");
        if (hasL3) headers.Add("Group 3");
        headers.AddRange(new[] {
            "Offer No", "Date", "Valid Until", "Status",
            "Customer", "Store", "Agent",
            "Items", "Qty", "Subtotal", "Disc.",
            "VAT", "Grand Total"
        });
        if (viewCost) headers.Add("Cost");
        headers.AddRange(new[] {
            "Order %",
            "Lead", "Printed", "Emailed", "Std Offer", "Comments", "Internal Notes", "Source"
        });

        foreach (var h in headers)
        {
            var cell = new PdfPCell(new Phrase(h, HeaderFont))
            {
                BackgroundColor = new BaseColor(124, 58, 237),
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 4f
            };
            table.AddCell(cell);
        }

        var subBg = new BaseColor(237, 233, 254);
        var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.DarkGray);

        bool alternate = false;
        void WriteRow(OffersReportRow row)
        {
            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            if (hasL1) AddDataCell(table, row.Level1Descr, bg);
            if (hasL2) AddDataCell(table, row.Level2Descr, bg);
            if (hasL3) AddDataCell(table, row.Level3Descr, bg);
            AddDataCell(table, row.OfferNo, bg);
            AddDataCell(table, row.DateTrans?.ToString("yyyy-MM-dd") ?? "", bg);
            AddDataCell(table, row.ValidUntil?.ToString("yyyy-MM-dd") ?? "", bg);
            AddDataCell(table, row.StatusName, bg);
            AddDataCell(table, row.CustomerName, bg);
            AddDataCell(table, row.StoreName, bg);
            AddDataCell(table, row.AgentName, bg);
            AddDataCell(table, row.ItemCount.ToString(), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.TotalQuantity.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.InvoiceTotal.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.InvoiceTotalDiscount.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.InvoiceVat.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.InvoiceGrandTotal.ToString("N2"), bg, Element.ALIGN_RIGHT);
            if (viewCost) AddDataCell(table, row.TotalItemCost.ToString("N2"), bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.OrderPercentage.ToString("N1") + "%", bg, Element.ALIGN_RIGHT);
            AddDataCell(table, row.LinkedLead, bg);
            AddDataCell(table, row.Printed ? "Yes" : "", bg);
            AddDataCell(table, row.SentByEmail ? "Yes" : "", bg);
            AddDataCell(table, row.IsStandardOffer ? row.StandardOfferName : "", bg);
            AddDataCell(table, row.Comments, bg);
            AddDataCell(table, row.InternalNotes, bg);
            AddDataCell(table, row.Source, bg);
            alternate = !alternate;
        }

        void WriteSub(List<OffersReportRow> grp, string label)
        {
            // Label spans group cols + Offer No..Agent (7); then 6 additive metrics (+Cost);
            // then Order % and the trailing text columns (blank).
            AddSubtotalLabelCell(table, label, g + 7, subBg, subFont);
            AddSubtotalCell(table, grp.Sum(x => x.ItemCount).ToString(), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.TotalQuantity).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.InvoiceTotal).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.InvoiceTotalDiscount).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.InvoiceVat).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            AddSubtotalCell(table, grp.Sum(x => x.InvoiceGrandTotal).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            if (viewCost) AddSubtotalCell(table, grp.Sum(x => x.TotalItemCost).ToString("N2"), subBg, subFont, Element.ALIGN_RIGHT);
            // Order % + Lead, Printed, Emailed, Std Offer, Comments, Internal Notes, Source = 8 blanks
            for (int i = 0; i < 8; i++) AddSubtotalCell(table, "", subBg, subFont);
        }

        if (hasL1)
        {
            foreach (var l1 in rows.GroupBy(r => r.Level1Descr ?? r.Level1Code ?? "N/A"))
            {
                var l1Rows = l1.ToList();
                if (hasL2)
                {
                    foreach (var l2 in l1Rows.GroupBy(r => r.Level2Descr ?? r.Level2Code ?? "N/A"))
                    {
                        var l2Rows = l2.ToList();
                        if (hasL3)
                        {
                            foreach (var l3 in l2Rows.GroupBy(r => r.Level3Descr ?? r.Level3Code ?? "N/A"))
                            {
                                var l3Rows = l3.ToList();
                                foreach (var row in l3Rows) WriteRow(row);
                                WriteSub(l3Rows, l3.Key + " subtotal");
                            }
                        }
                        else
                        {
                            foreach (var row in l2Rows) WriteRow(row);
                        }
                        WriteSub(l2Rows, l2.Key + " subtotal");
                    }
                }
                else
                {
                    foreach (var row in l1Rows) WriteRow(row);
                }
                WriteSub(l1Rows, l1.Key + " total");
            }
        }
        else
        {
            foreach (var row in rows) WriteRow(row);
        }

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    public byte[] GenerateTrialBalancePdf(List<TrialBalanceRow> rows, TrialBalanceFilter filter)
    {
        rows ??= new();
        bool isSummary = filter.ReportMode == TrialBalanceReportMode.Summary;
        int colCount = isSummary ? 8 : 10;

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Trial Balance", TitleFont));
        document.Add(new Paragraph($"As At: {filter.AsAt:dd/MM/yyyy}", SubtitleFont));
        document.Add(new Paragraph($"Report Mode: {filter.ReportMode} | Include Zero Movements: {(filter.IncludeZeroMovements ? "Yes" : "No")}", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 5f };
        table.SetWidths(isSummary
            ? new float[] { 12, 30, 12, 12, 12, 12, 12, 12 }
            : new float[] { 10, 22, 10, 22, 9, 9, 9, 9, 9, 9 });

        AddHeaderCell(table, "Header Code");
        AddHeaderCell(table, "Header");
        if (!isSummary)
        {
            AddHeaderCell(table, "Account Code");
            AddHeaderCell(table, "Account");
        }
        AddHeaderCell(table, "Opening DR");
        AddHeaderCell(table, "Opening CR");
        AddHeaderCell(table, "Debit");
        AddHeaderCell(table, "Credit");
        AddHeaderCell(table, "Closing DR");
        AddHeaderCell(table, "Closing CR");

        static string Num(decimal v) => v == 0 ? "" : v.ToString("N2");
        bool alternate = false;

        if (isSummary)
        {
            foreach (var g in rows.GroupBy(r => new { r.HeaderKey, r.HeaderCode, r.HeaderName })
                                   .OrderBy(g => g.First().HeaderCodeSort, StringComparer.Ordinal))
            {
                var list = g.ToList();
                var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
                AddDataCell(table, g.Key.HeaderCode, bg);
                AddDataCell(table, g.Key.HeaderName, bg);
                AddDataCell(table, Num(list.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance)), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(list.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance)), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(list.Sum(r => r.DebitMovement)), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(list.Sum(r => r.CreditMovement)), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(list.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance)), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(list.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance)), bg, Element.ALIGN_RIGHT);
                alternate = !alternate;
            }
        }
        else
        {
            foreach (var row in rows)
            {
                var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
                AddDataCell(table, row.HeaderCode, bg);
                AddDataCell(table, row.HeaderName, bg);
                AddDataCell(table, row.AccountCode, bg);
                AddDataCell(table, row.AccountName, bg);
                AddDataCell(table, Num(row.OpeningBalanceType == "DR" ? row.OpeningBalance : 0), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(row.OpeningBalanceType == "CR" ? row.OpeningBalance : 0), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(row.DebitMovement), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(row.CreditMovement), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(row.ClosingBalanceType == "DR" ? row.ClosingBalance : 0), bg, Element.ALIGN_RIGHT);
                AddDataCell(table, Num(row.ClosingBalanceType == "CR" ? row.ClosingBalance : 0), bg, Element.ALIGN_RIGHT);
                alternate = !alternate;
            }
        }

        // Grand totals.
        AddTotalCell(table, "TOTAL");
        AddTotalCell(table, "");
        if (!isSummary) { AddTotalCell(table, ""); AddTotalCell(table, ""); }
        AddTotalCell(table, Num(rows.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance)), Element.ALIGN_RIGHT);
        AddTotalCell(table, Num(rows.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance)), Element.ALIGN_RIGHT);
        AddTotalCell(table, Num(rows.Sum(r => r.DebitMovement)), Element.ALIGN_RIGHT);
        AddTotalCell(table, Num(rows.Sum(r => r.CreditMovement)), Element.ALIGN_RIGHT);
        AddTotalCell(table, Num(rows.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance)), Element.ALIGN_RIGHT);
        AddTotalCell(table, Num(rows.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance)), Element.ALIGN_RIGHT);

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }

    public byte[] GenerateProfitLossPdf(List<ProfitLossRow> rows, ProfitLossFilter filter)
    {
        rows ??= new();
        bool compare = filter.CompareToLastYear;
        var visible = rows.Where(r => !r.Suppressed).ToList();
        int colCount = compare ? 5 : 3;

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4, 24, 24, 30, 24);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Profit & Loss", TitleFont));
        document.Add(new Paragraph($"Period: {filter.DateFrom:dd/MM/yyyy} - {filter.DateTo:dd/MM/yyyy}", SubtitleFont));
        document.Add(new Paragraph($"Header Level: {(filter.HeaderLevel ? "Yes" : "No")}", SubtitleFont));
        if (compare)
            document.Add(new Paragraph($"Comparison Period: {filter.PriorDateFrom:dd/MM/yyyy} - {filter.PriorDateTo:dd/MM/yyyy}", SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 5f };
        table.SetWidths(compare
            ? new float[] { 34, 14, 14, 14, 14 }
            : new float[] { 60, 20, 20 });

        AddHeaderCell(table, "Account");
        AddHeaderCell(table, "Amount");
        if (compare)
        {
            AddHeaderCell(table, "Prior Year");
            AddHeaderCell(table, "Variance");
            AddHeaderCell(table, "Variance %");
        }

        static string Num(decimal v) => v == 0 ? "" : v.ToString("N2");
        static string Pct(decimal? v) => v.HasValue ? v.Value.ToString("N2") + "%" : "";
        bool alternate = false;

        foreach (var grp in visible.GroupBy(r => r.Group).OrderBy(g => (int)g.Key))
        {
            var list = grp.ToList();

            // Group header row.
            var hc = new PdfPCell(new Phrase(list[0].GroupName, SubtitleFont)) { Colspan = colCount, BackgroundColor = new BaseColor(226, 232, 240), Padding = 4f };
            table.AddCell(hc);

            foreach (var row in list)
            {
                var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
                AddDataCell(table, "    " + (string.IsNullOrEmpty(row.AccountCode) ? row.AccountName : row.AccountCode + " - " + row.AccountName), bg);
                AddDataCell(table, Num(row.Balance), bg, Element.ALIGN_RIGHT);
                if (compare)
                {
                    AddDataCell(table, Num(row.PriorBalance), bg, Element.ALIGN_RIGHT);
                    AddDataCell(table, Num(row.Variance), bg, Element.ALIGN_RIGHT);
                    AddDataCell(table, Pct(row.VariancePercent), bg, Element.ALIGN_RIGHT);
                }
                alternate = !alternate;
            }

            // Group subtotal.
            AddTotalCell(table, list[0].GroupName + " subtotal");
            AddTotalCell(table, Num(list.Sum(r => r.Balance)), Element.ALIGN_RIGHT);
            if (compare)
            {
                AddTotalCell(table, Num(list.Sum(r => r.PriorBalance)), Element.ALIGN_RIGHT);
                AddTotalCell(table, Num(list.Sum(r => r.Variance)), Element.ALIGN_RIGHT);
                AddTotalCell(table, "", Element.ALIGN_RIGHT);
            }
        }

        decimal Tot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.Balance);
        decimal PriorTot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.PriorBalance);
        decimal gross = Tot(ProfitLossGroup.Sales) - Tot(ProfitLossGroup.CostOfSales);
        decimal net = gross + Tot(ProfitLossGroup.Income) - Tot(ProfitLossGroup.Expenses);
        decimal pGross = PriorTot(ProfitLossGroup.Sales) - PriorTot(ProfitLossGroup.CostOfSales);
        decimal pNet = pGross + PriorTot(ProfitLossGroup.Income) - PriorTot(ProfitLossGroup.Expenses);

        void SummaryRow(string label, decimal cur, decimal prior)
        {
            AddTotalCell(table, label);
            AddTotalCell(table, Num(cur), Element.ALIGN_RIGHT);
            if (compare)
            {
                AddTotalCell(table, Num(prior), Element.ALIGN_RIGHT);
                AddTotalCell(table, Num(cur - prior), Element.ALIGN_RIGHT);
                AddTotalCell(table, "", Element.ALIGN_RIGHT);
            }
        }
        SummaryRow("GROSS PROFIT", gross, pGross);
        SummaryRow("NET PROFIT", net, pNet);

        document.Add(table);
        document.Close();
        return ms.ToArray();
    }
}
