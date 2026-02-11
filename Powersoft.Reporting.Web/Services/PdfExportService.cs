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
}
