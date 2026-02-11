using ClosedXML.Excel;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services;

public class ExcelExportService
{
    public byte[] GenerateAverageBasketExcel(
        List<AverageBasketRow> rows,
        ReportGrandTotals? grandTotals,
        ReportFilter filter)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Average Basket");
        
        // Title
        ws.Cell(1, 1).Value = "Average Basket Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        
        ws.Cell(2, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = $"Breakdown: {filter.Breakdown} | Group By: {filter.GroupBy}";
        ws.Cell(4, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        
        int headerRow = 6;
        int col = 1;
        bool hasGrouping = filter.GroupBy != Core.Enums.GroupByType.None;
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;
        
        // Headers
        if (hasGrouping) ws.Cell(headerRow, col++).Value = filter.GroupBy.ToString();
        ws.Cell(headerRow, col++).Value = "Period";
        ws.Cell(headerRow, col++).Value = "Invoices";
        ws.Cell(headerRow, col++).Value = "Returns";
        ws.Cell(headerRow, col++).Value = "Net Trans.";
        ws.Cell(headerRow, col++).Value = "Qty Sold";
        ws.Cell(headerRow, col++).Value = "Qty Ret.";
        ws.Cell(headerRow, col++).Value = "Net Qty";
        ws.Cell(headerRow, col++).Value = includeVat ? "Gross Sales" : "Net Sales";
        ws.Cell(headerRow, col++).Value = "Avg Basket";
        ws.Cell(headerRow, col++).Value = "Avg Qty";
        
        if (compareLY)
        {
            ws.Cell(headerRow, col++).Value = "LY Sales";
            ws.Cell(headerRow, col++).Value = "LY Avg";
            ws.Cell(headerRow, col++).Value = "YoY %";
        }
        
        // Style header row
        var headerRange = ws.Range(headerRow, 1, headerRow, col - 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        
        // Data rows
        int dataRow = headerRow + 1;
        foreach (var row in rows)
        {
            col = 1;
            if (hasGrouping)
                ws.Cell(dataRow, col++).Value = row.Level1Value ?? row.Level1 ?? "N/A";
            
            ws.Cell(dataRow, col++).Value = row.Period;
            ws.Cell(dataRow, col++).Value = row.CYInvoiceCount;
            ws.Cell(dataRow, col++).Value = row.CYCreditCount;
            ws.Cell(dataRow, col++).Value = row.CYTotalTransactions;
            ws.Cell(dataRow, col++).Value = row.CYQtySold;
            ws.Cell(dataRow, col++).Value = row.CYQtyReturned;
            ws.Cell(dataRow, col++).Value = row.CYTotalQty;
            ws.Cell(dataRow, col++).Value = includeVat ? row.CYTotalGross : row.CYTotalNet;
            ws.Cell(dataRow, col++).Value = includeVat ? row.CYAverageGross : row.CYAverageNet;
            ws.Cell(dataRow, col++).Value = row.CYAverageQty;
            
            if (compareLY)
            {
                ws.Cell(dataRow, col++).Value = includeVat ? row.LYTotalGross : row.LYTotalNet;
                ws.Cell(dataRow, col++).Value = includeVat ? row.LYAverageGross : row.LYAverageNet;
                ws.Cell(dataRow, col++).Value = row.YoYChangePercent;
            }
            
            dataRow++;
        }
        
        // Grand total row
        if (grandTotals != null)
        {
            col = 1;
            if (hasGrouping) ws.Cell(dataRow, col++).Value = "";
            ws.Cell(dataRow, col++).Value = "GRAND TOTAL";
            ws.Cell(dataRow, col++).Value = grandTotals.TotalInvoices;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalCredits;
            ws.Cell(dataRow, col++).Value = grandTotals.NetTransactions;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalQtySold;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalQtyReturned;
            ws.Cell(dataRow, col++).Value = grandTotals.NetQty;
            ws.Cell(dataRow, col++).Value = includeVat ? grandTotals.GrossSales : grandTotals.NetSales;
            ws.Cell(dataRow, col++).Value = includeVat ? grandTotals.AverageBasketGross : grandTotals.AverageBasketNet;
            ws.Cell(dataRow, col++).Value = grandTotals.AverageQty;
            
            if (compareLY)
            {
                ws.Cell(dataRow, col++).Value = includeVat ? grandTotals.LYTotalGross : grandTotals.LYTotalNet;
                ws.Cell(dataRow, col++).Value = includeVat ? grandTotals.LYAverageBasketGross : grandTotals.LYAverageBasketNet;
                ws.Cell(dataRow, col++).Value = grandTotals.YoYChangePercent;
            }
            
            var totalRange = ws.Range(dataRow, 1, dataRow, col - 1);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        }
        
        // Number formatting
        int totalCols = col - 1;
        int moneyStartCol = hasGrouping ? 9 : 8;
        for (int r = headerRow + 1; r <= dataRow; r++)
        {
            ws.Cell(r, moneyStartCol).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, moneyStartCol + 1).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, moneyStartCol + 2).Style.NumberFormat.Format = "#,##0.00";
        }
        
        // Auto-fit columns
        ws.Columns().AdjustToContents();
        
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
