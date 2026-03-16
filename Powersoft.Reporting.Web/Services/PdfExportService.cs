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
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;

        int colCount = 10 + (hasGrouping ? 1 : 0) + (compareLY ? 3 : 0);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        // Title
        document.Add(new Paragraph("Average Basket Report", TitleFont));
        document.Add(new Paragraph(
            $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd} | Breakdown: {filter.Breakdown} | Group By: {filter.GroupBy}",
            SubtitleFont));
        document.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", SubtitleFont));
        document.Add(new Paragraph(" "));

        var table = new PdfPTable(colCount)
        {
            WidthPercentage = 100,
            SpacingBefore = 5f
        };

        // Column widths
        var widths = BuildColumnWidths(hasGrouping, compareLY, colCount);
        table.SetWidths(widths);

        // Headers
        if (hasGrouping) AddHeaderCell(table, filter.GroupBy.ToString());
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
        foreach (var row in rows)
        {
            var bg = alternate ? new BaseColor(248, 250, 252) : BaseColor.White;
            
            if (hasGrouping) AddDataCell(table, row.Level1Value ?? row.Level1 ?? "N/A", bg);
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

        // Grand total
        if (grandTotals != null)
        {
            if (hasGrouping) AddTotalCell(table, "");
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

    private float[] BuildColumnWidths(bool hasGrouping, bool compareLY, int colCount)
    {
        var widths = new List<float>();
        if (hasGrouping) widths.Add(12f);
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
                     + (hasItem ? 2 : 0) + 7 + (filter.ShowStock ? 1 : 0);

        using var ms = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 20, 20, 30, 20);
        PdfWriter.GetInstance(document, ms);
        document.Open();

        document.Add(new Paragraph("Purchases vs Sales Report", TitleFont));
        document.Add(new Paragraph(
            $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd} | Mode: {filter.ReportMode}",
            SubtitleFont));
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
}
