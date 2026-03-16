using ClosedXML.Excel;
using Powersoft.Reporting.Core.Enums;
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
        
        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Breakdown: {filter.Breakdown}";
        ws.Cell(selRow++, 1).Value = $"Group By: {filter.GroupBy}";
        if (filter.SecondaryGroupBy != GroupByType.None)
            ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroupBy}";
        ws.Cell(selRow++, 1).Value = $"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}";
        if (filter.CompareLastYear)
            ws.Cell(selRow++, 1).Value = "Compare Last Year: Yes";
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            ws.Cell(selRow++, 1).Value = $"Stores: {string.Join(", ", filter.StoreCodes)}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");
        
        int headerRow = selRow + 1;
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

    public byte[] GeneratePurchasesSalesExcel(
        List<PurchasesSalesRow> rows,
        PurchasesSalesTotals? totals,
        PurchasesSalesFilter filter)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Purchases vs Sales");

        ws.Cell(1, 1).Value = "Purchases vs Sales Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Mode: {filter.ReportMode}";
        if (filter.PrimaryGroup != PsGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Primary Group: {filter.PrimaryGroup}";
        if (filter.SecondaryGroup != PsGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroup}";
        if (filter.ThirdGroup != PsGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Third Group: {filter.ThirdGroup}";
        ws.Cell(selRow++, 1).Value = $"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}";
        if (filter.ShowProfit) ws.Cell(selRow++, 1).Value = "Show Profit: Yes";
        if (filter.ShowStock) ws.Cell(selRow++, 1).Value = "Show Stock: Yes";
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            ws.Cell(selRow++, 1).Value = $"Stores: {string.Join(", ", filter.StoreCodes)}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;

        bool hasL1 = filter.PrimaryGroup != PsGroupBy.None;
        bool hasL2 = filter.SecondaryGroup != PsGroupBy.None;
        bool hasL3 = filter.ThirdGroup != PsGroupBy.None;
        bool hasItem = !filter.IsSummary || (!hasL1 && !hasL2 && !hasL3);

        if (hasL1) ws.Cell(headerRow, col++).Value = filter.PrimaryGroup.ToString();
        if (hasL2) ws.Cell(headerRow, col++).Value = filter.SecondaryGroup.ToString();
        if (hasL3) ws.Cell(headerRow, col++).Value = filter.ThirdGroup.ToString();
        if (hasItem) { ws.Cell(headerRow, col++).Value = "Item Code"; ws.Cell(headerRow, col++).Value = "Item Name"; }
        ws.Cell(headerRow, col++).Value = "Qty Purchased";
        ws.Cell(headerRow, col++).Value = filter.IncludeVat ? "Gross Purchased" : "Net Purchased";
        ws.Cell(headerRow, col++).Value = "Qty Sold";
        ws.Cell(headerRow, col++).Value = filter.IncludeVat ? "Gross Sold" : "Net Sold";
        ws.Cell(headerRow, col++).Value = "Profit";
        ws.Cell(headerRow, col++).Value = "Qty %";
        ws.Cell(headerRow, col++).Value = "Val %";
        if (filter.ShowStock) ws.Cell(headerRow, col++).Value = "Stock Qty";

        var headerRange = ws.Range(headerRow, 1, headerRow, col - 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        int totalCols = col - 1;
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

            if (l3Changed)
                dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL3}", l3Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (l2Changed)
                dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL2}", l2Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (l1Changed)
                dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL1}", l1Agg, filter, hasL1, hasL2, hasL3, hasItem);

            if (l1Changed || ri == 0) { l1Agg = new SubtotalAgg(); l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l2Changed) { l2Agg = new SubtotalAgg(); l3Agg = new SubtotalAgg(); }
            else if (l3Changed) { l3Agg = new SubtotalAgg(); }
            prevL1 = curL1; prevL2 = curL2; prevL3 = curL3;

            l1Agg.Add(row, filter.IncludeVat); l2Agg.Add(row, filter.IncludeVat); l3Agg.Add(row, filter.IncludeVat);

            col = 1;
            if (hasL1) ws.Cell(dataRow, col++).Value = curL1;
            if (hasL2) ws.Cell(dataRow, col++).Value = curL2;
            if (hasL3) ws.Cell(dataRow, col++).Value = curL3;
            if (hasItem) { ws.Cell(dataRow, col++).Value = row.ItemCode ?? ""; ws.Cell(dataRow, col++).Value = row.ItemName ?? ""; }
            ws.Cell(dataRow, col++).Value = row.QuantityPurchased;
            ws.Cell(dataRow, col++).Value = filter.IncludeVat ? row.GrossPurchasedValue : row.NetPurchasedValue;
            ws.Cell(dataRow, col++).Value = row.QuantitySold;
            ws.Cell(dataRow, col++).Value = filter.IncludeVat ? row.GrossSoldValue : row.NetSoldValue;
            ws.Cell(dataRow, col++).Value = row.Profit;
            ws.Cell(dataRow, col++).Value = row.QtyPercent;
            ws.Cell(dataRow, col++).Value = row.ValPercent;
            if (filter.ShowStock) ws.Cell(dataRow, col++).Value = row.TotalStockQty;
            dataRow++;
        }

        if (rows.Count > 0)
        {
            if (hasL3) dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL3}", l3Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (hasL2) dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL2}", l2Agg, filter, hasL1, hasL2, hasL3, hasItem);
            if (hasL1) dataRow = WriteExcelSubtotalRow(ws, dataRow, totalCols, $"Subtotal: {prevL1}", l1Agg, filter, hasL1, hasL2, hasL3, hasItem);
        }

        if (totals != null)
        {
            col = 1;
            int skipCols = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
            for (int i = 0; i < skipCols; i++) ws.Cell(dataRow, col++).Value = "";
            if (hasItem) { ws.Cell(dataRow, col++).Value = "TOTAL"; ws.Cell(dataRow, col++).Value = ""; }
            else ws.Cell(dataRow, col++).Value = "TOTAL";
            ws.Cell(dataRow, col++).Value = totals.TotalQtyPurchased;
            ws.Cell(dataRow, col++).Value = filter.IncludeVat ? totals.TotalGrossPurchased : totals.TotalNetPurchased;
            ws.Cell(dataRow, col++).Value = totals.TotalQtySold;
            ws.Cell(dataRow, col++).Value = filter.IncludeVat ? totals.TotalGrossSold : totals.TotalNetSold;
            ws.Cell(dataRow, col++).Value = totals.TotalProfit;
            ws.Cell(dataRow, col++).Value = totals.QtyPercent;
            ws.Cell(dataRow, col++).Value = totals.ValPercent;
            if (filter.ShowStock) ws.Cell(dataRow, col++).Value = totals.TotalStockQty;

            var totalRange = ws.Range(dataRow, 1, dataRow, col - 1);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private int WriteExcelSubtotalRow(IXLWorksheet ws, int dataRow, int totalCols,
        string label, SubtotalAgg agg, PurchasesSalesFilter filter,
        bool hasL1, bool hasL2, bool hasL3, bool hasItem)
    {
        int col = 1;
        int skipCols = (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);
        for (int i = 0; i < skipCols; i++) ws.Cell(dataRow, col++).Value = "";
        if (hasItem) { ws.Cell(dataRow, col++).Value = label; ws.Cell(dataRow, col++).Value = ""; }
        else ws.Cell(dataRow, col++).Value = label;
        ws.Cell(dataRow, col++).Value = agg.QtyPurchased;
        ws.Cell(dataRow, col++).Value = agg.ValPurchased;
        ws.Cell(dataRow, col++).Value = agg.QtySold;
        ws.Cell(dataRow, col++).Value = agg.ValSold;
        ws.Cell(dataRow, col++).Value = agg.Profit;
        ws.Cell(dataRow, col++).Value = agg.QtyPct;
        ws.Cell(dataRow, col++).Value = agg.ValPct;
        if (filter.ShowStock) ws.Cell(dataRow, col++).Value = agg.StockQty;

        var range = ws.Range(dataRow, 1, dataRow, totalCols);
        range.Style.Font.Bold = true;
        range.Style.Font.Italic = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
        return dataRow + 1;
    }
}

