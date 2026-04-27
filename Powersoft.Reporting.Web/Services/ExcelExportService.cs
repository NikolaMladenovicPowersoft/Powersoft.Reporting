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

    public byte[] GenerateCatalogueExcel(
        List<CatalogueRow> rows,
        CatalogueTotals? totals,
        CatalogueFilter filter)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Catalogue");

        ws.Cell(1, 1).Value = "Power Reports Catalogue";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Report Mode: {filter.ReportMode}";
        ws.Cell(selRow++, 1).Value = $"Report On: {filter.ReportOn}";
        if (filter.PrimaryGroup != Core.Enums.CatalogueGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Primary Group: {filter.PrimaryGroup}";
        if (filter.SecondaryGroup != Core.Enums.CatalogueGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroup}";
        if (filter.ThirdGroup != Core.Enums.CatalogueGroupBy.None)
            ws.Cell(selRow++, 1).Value = $"Third Group: {filter.ThirdGroup}";
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            ws.Cell(selRow++, 1).Value = $"Stores: {string.Join(", ", filter.StoreCodes)}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        bool hasL1 = filter.PrimaryGroup != Core.Enums.CatalogueGroupBy.None;
        bool hasL2 = filter.SecondaryGroup != Core.Enums.CatalogueGroupBy.None;
        bool hasL3 = filter.ThirdGroup != Core.Enums.CatalogueGroupBy.None;
        bool isSummary = filter.IsSummary;
        bool showItem = !isSummary || (!hasL1 && !hasL2 && !hasL3);
        bool dc(string c) => filter.DisplayColumns.Contains(c, StringComparer.OrdinalIgnoreCase);

        var cols = new List<(string Key, string Label, bool IsNumeric, Func<CatalogueRow, object?> Value)>();
        if (hasL1) cols.Add(("_L1", "Group 1", false, r => r.Level1Value));
        if (hasL2) cols.Add(("_L2", "Group 2", false, r => r.Level2Value));
        if (hasL3) cols.Add(("_L3", "Group 3", false, r => r.Level3Value));
        if (showItem && dc("ItemCode")) cols.Add(("ItemCode", "Code", false, r => r.ItemCode));
        if (showItem && dc("MainBarcode")) cols.Add(("MainBarcode", "Barcode", false, r => r.MainBarcode));
        if (showItem && dc("ItemName")) cols.Add(("ItemName", "Description", false, r => r.ItemDescription));
        if (dc("Quantity")) cols.Add(("Quantity", "Qty", true, r => r.Quantity));
        if (dc("Value")) cols.Add(("Value", "Value", true, r => r.ValueBeforeDiscount));
        if (dc("Discount")) cols.Add(("Discount", "Discount", true, r => r.Discount));
        if (dc("NetValue")) cols.Add(("NetValue", "Net Value", true, r => r.NetValue));
        if (dc("VatAmount")) cols.Add(("VatAmount", "VAT", true, r => r.VatAmount));
        if (dc("GrossAmount")) cols.Add(("GrossAmount", "Gross Amt", true, r => r.GrossAmount));
        if (dc("Profit")) cols.Add(("Profit", "Profit", true, r => r.ProfitValue));
        if (dc("Markup")) cols.Add(("Markup", "Markup %", true, r => r.Markup));
        if (dc("Margin")) cols.Add(("Margin", "Margin %", true, r => r.Margin));
        if (dc("Cost")) cols.Add(("Cost", "Cost", true, r => r.Cost));
        if (dc("TotalCost")) cols.Add(("TotalCost", "Total Cost", true, r => r.TotalCost));
        if (dc("TotalStockQty")) cols.Add(("TotalStockQty", "Stock Qty", true, r => r.TotalStockQty));
        if (dc("TotalStockValue")) cols.Add(("TotalStockValue", "Stock Value", true, r => r.TotalStockValue));
        if (dc("EntityCode")) cols.Add(("EntityCode", "Entity Code", false, r => r.EntityCode));
        if (dc("EntityName")) cols.Add(("EntityName", "Entity Name", false, r => r.EntityName));
        if (dc("InvoiceNumber")) cols.Add(("InvoiceNumber", "Invoice No", false, r => r.InvoiceNumber));
        if (dc("InvoiceType")) cols.Add(("InvoiceType", "Inv. Type", false, r => r.InvoiceType));
        if (dc("StoreCode")) cols.Add(("StoreCode", "Store Code", false, r => r.StoreCode));
        if (dc("StoreName")) cols.Add(("StoreName", "Store", false, r => r.StoreName));
        if (dc("StationCode")) cols.Add(("StationCode", "Station", false, r => r.StationCode));
        if (dc("DateTrans")) cols.Add(("DateTrans", "Date", false, r => r.DateTrans?.ToString("yyyy-MM-dd") ?? ""));
        if (dc("UserCode")) cols.Add(("UserCode", "User", false, r => r.UserCode));
        if (dc("AgentName")) cols.Add(("AgentName", "Agent", false, r => r.AgentName));
        if (dc("ZReportNumber")) cols.Add(("ZReportNumber", "Z Report", false, r => r.ZReportNumber));
        if (dc("PaymentType")) cols.Add(("PaymentType", "Payment Type", false, r => r.PaymentType));
        if (dc("ItemCategory")) cols.Add(("ItemCategory", "Category", false, r => r.ItemCategoryDescr));
        if (dc("ItemDepartment")) cols.Add(("ItemDepartment", "Department", false, r => r.ItemDepartmentDescr));
        if (dc("Brand")) cols.Add(("Brand", "Brand", false, r => r.BrandName));
        if (dc("Season")) cols.Add(("Season", "Season", false, r => r.SeasonName));
        if (dc("Model")) cols.Add(("Model", "Model", false, r => r.ModelCode));
        if (dc("Colour")) cols.Add(("Colour", "Colour", false, r => r.Colour));
        if (dc("Size")) cols.Add(("Size", "Size", false, r => r.Size));
        if (dc("Franchise")) cols.Add(("Franchise", "Franchise", false, r => r.FranchiseName));
        if (dc("ItemSupplier")) cols.Add(("ItemSupplier", "Supplier", false, r => r.ItemSupplierName));
        if (dc("Price1Excl")) cols.Add(("Price1Excl", "Price 1 Ex", true, r => r.Price1Excl));
        if (dc("Price1Incl")) cols.Add(("Price1Incl", "Price 1 In", true, r => r.Price1Incl));
        if (dc("Price2Excl")) cols.Add(("Price2Excl", "Price 2 Ex", true, r => r.Price2Excl));
        if (dc("Price2Incl")) cols.Add(("Price2Incl", "Price 2 In", true, r => r.Price2Incl));
        if (dc("Price3Excl")) cols.Add(("Price3Excl", "Price 3 Ex", true, r => r.Price3Excl));
        if (dc("Price3Incl")) cols.Add(("Price3Incl", "Price 3 In", true, r => r.Price3Incl));
        if (dc("ItemAttr1")) cols.Add(("ItemAttr1", "Attr 1", false, r => r.ItemAttr1Descr));
        if (dc("ItemAttr2")) cols.Add(("ItemAttr2", "Attr 2", false, r => r.ItemAttr2Descr));
        if (dc("ItemAttr3")) cols.Add(("ItemAttr3", "Attr 3", false, r => r.ItemAttr3Descr));
        if (dc("ItemAttr4")) cols.Add(("ItemAttr4", "Attr 4", false, r => r.ItemAttr4Descr));
        if (dc("ItemAttr5")) cols.Add(("ItemAttr5", "Attr 5", false, r => r.ItemAttr5Descr));
        if (dc("ItemAttr6")) cols.Add(("ItemAttr6", "Attr 6", false, r => r.ItemAttr6Descr));

        for (int i = 0; i < cols.Count; i++)
            ws.Cell(headerRow, i + 1).Value = cols[i].Label;

        if (cols.Count > 0)
        {
            var headerRange = ws.Range(headerRow, 1, headerRow, cols.Count);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int dataRow = headerRow + 1;
        foreach (var row in rows)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                var val = cols[i].Value(row);
                if (val is decimal dec) ws.Cell(dataRow, i + 1).Value = dec;
                else if (val is int iv) ws.Cell(dataRow, i + 1).Value = iv;
                else if (val is DateTime dt) ws.Cell(dataRow, i + 1).Value = dt;
                else ws.Cell(dataRow, i + 1).Value = val?.ToString() ?? "";
                if (cols[i].IsNumeric)
                    ws.Cell(dataRow, i + 1).Style.NumberFormat.Format = "#,##0.00";
            }
            dataRow++;
        }

        if (totals != null && cols.Count > 0)
        {
            var totalMap = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity"] = totals.TotalQuantity,
                ["Value"] = totals.TotalValueBeforeDiscount,
                ["Discount"] = totals.TotalDiscount,
                ["NetValue"] = totals.TotalNetValue,
                ["VatAmount"] = totals.TotalVatAmount,
                ["GrossAmount"] = totals.TotalGrossAmount,
                ["Profit"] = totals.TotalProfitValue,
                ["Markup"] = totals.TotalMarkup,
                ["Margin"] = totals.TotalMargin,
                ["TotalCost"] = totals.TotalTotalCost,
                ["TotalStockQty"] = totals.TotalStockQty,
                ["TotalStockValue"] = totals.TotalStockValue
            };

            bool labelPlaced = false;
            for (int i = 0; i < cols.Count; i++)
            {
                if (totalMap.TryGetValue(cols[i].Key, out var v) && v.HasValue)
                {
                    ws.Cell(dataRow, i + 1).Value = v.Value;
                    ws.Cell(dataRow, i + 1).Style.NumberFormat.Format = "#,##0.00";
                }
                else if (!labelPlaced && !cols[i].IsNumeric)
                {
                    ws.Cell(dataRow, i + 1).Value = i == 0 ? "GRAND TOTAL" : "";
                    if (i == 0) labelPlaced = true;
                }
            }
            if (!labelPlaced) ws.Cell(dataRow, 1).Value = "GRAND TOTAL";

            var totalRange = ws.Range(dataRow, 1, dataRow, cols.Count);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateChartExcel(List<ChartDataPoint> data, ChartFilter filter)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Chart Data");

        ws.Cell(1, 1).Value = "Charts & Dashboards — Data Export";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Dimension: {filter.Dimension}";
        ws.Cell(selRow++, 1).Value = $"Metric: {filter.Metric}";
        ws.Cell(selRow++, 1).Value = $"Top N: {filter.TopN}";
        ws.Cell(selRow++, 1).Value = $"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}";
        if (filter.CompareLastYear)
            ws.Cell(selRow++, 1).Value = "Compare Last Year: Yes";
        if (filter.ShowOthers)
            ws.Cell(selRow++, 1).Value = "Show Others: Yes";
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            ws.Cell(selRow++, 1).Value = $"Stores: {string.Join(", ", filter.StoreCodes)}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        bool hasCompare = filter.CompareLastYear && data.Any(d => d.CompareValue.HasValue);
        int colCount = hasCompare ? 5 : 3;

        ws.Cell(headerRow, 1).Value = "#";
        ws.Cell(headerRow, 2).Value = filter.Dimension.ToString();
        ws.Cell(headerRow, 3).Value = filter.Metric == ChartMetric.Quantity ? "Quantity" : "Value";
        if (hasCompare)
        {
            ws.Cell(headerRow, 4).Value = "Last Year";
            ws.Cell(headerRow, 5).Value = "YoY %";
        }

        var headerRange = ws.Range(headerRow, 1, headerRow, colCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        decimal total = data.Sum(d => d.Value);
        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            ws.Cell(dataRow, 1).Value = i + 1;
            ws.Cell(dataRow, 2).Value = d.Label;
            ws.Cell(dataRow, 3).Value = d.Value;
            ws.Cell(dataRow, 3).Style.NumberFormat.Format = filter.Metric == ChartMetric.Quantity ? "#,##0" : "#,##0.00";
            if (hasCompare)
            {
                ws.Cell(dataRow, 4).Value = d.CompareValue ?? 0;
                ws.Cell(dataRow, 4).Style.NumberFormat.Format = filter.Metric == ChartMetric.Quantity ? "#,##0" : "#,##0.00";
                var yoy = d.CompareValue.HasValue && d.CompareValue.Value > 0
                    ? (d.Value - d.CompareValue.Value) / d.CompareValue.Value * 100 : 0;
                ws.Cell(dataRow, 5).Value = yoy;
                ws.Cell(dataRow, 5).Style.NumberFormat.Format = "0.0";
            }
            dataRow++;
        }

        ws.Cell(dataRow, 1).Value = "";
        ws.Cell(dataRow, 2).Value = "TOTAL";
        ws.Cell(dataRow, 3).Value = total;
        ws.Cell(dataRow, 3).Style.NumberFormat.Format = filter.Metric == ChartMetric.Quantity ? "#,##0" : "#,##0.00";
        var totalRange = ws.Range(dataRow, 1, dataRow, colCount);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateParetoExcel(ParetoResult result, ParetoFilter filter)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Pareto 80-20");

        ws.Cell(1, 1).Value = "Pareto 80/20 Analysis";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Dimension: {filter.Dimension}";
        ws.Cell(selRow++, 1).Value = $"Metric: {filter.Metric}";
        ws.Cell(selRow++, 1).Value = $"Include VAT: {(filter.IncludeVat ? "Yes" : "No")}";
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            ws.Cell(selRow++, 1).Value = $"Stores: {string.Join(", ", filter.StoreCodes)}";
        ws.Cell(selRow++, 1).Value = $"Class A Threshold: {filter.ClassAThreshold}%";
        ws.Cell(selRow++, 1).Value = $"Class B Threshold: {filter.ClassBThreshold}%";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int sRow = selRow;
        ws.Cell(sRow, 1).Value = "Summary";
        ws.Cell(sRow, 1).Style.Font.Bold = true;
        sRow++;
        ws.Cell(sRow, 1).Value = $"Grand Total: {result.GrandTotal:N2}";
        ws.Cell(sRow, 2).Value = $"Total Items: {result.Rows.Count}";
        sRow++;
        ws.Cell(sRow, 1).Value = $"Class A: {result.ClassACount} items ({(result.GrandTotal > 0 ? result.ClassAValue / result.GrandTotal * 100 : 0):N1}% of value)";
        sRow++;
        ws.Cell(sRow, 1).Value = $"Class B: {result.ClassBCount} items ({(result.GrandTotal > 0 ? result.ClassBValue / result.GrandTotal * 100 : 0):N1}% of value)";
        sRow++;
        ws.Cell(sRow, 1).Value = $"Class C: {result.ClassCCount} items ({(result.GrandTotal > 0 ? result.ClassCValue / result.GrandTotal * 100 : 0):N1}% of value)";

        int headerRow = sRow + 2;
        ws.Cell(headerRow, 1).Value = "#";
        ws.Cell(headerRow, 2).Value = "Code";
        ws.Cell(headerRow, 3).Value = "Name";
        ws.Cell(headerRow, 4).Value = filter.Metric == ParetoMetric.Quantity ? "Quantity" : "Value";
        ws.Cell(headerRow, 5).Value = "%";
        ws.Cell(headerRow, 6).Value = "Cumul. %";
        ws.Cell(headerRow, 7).Value = "Class";

        var headerRange = ws.Range(headerRow, 1, headerRow, 7);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        foreach (var row in result.Rows)
        {
            var bgColor = row.Classification switch
            {
                "A" => XLColor.FromHtml("#dcfce7"),
                "B" => XLColor.FromHtml("#fef9c3"),
                _ => XLColor.FromHtml("#fee2e2")
            };

            ws.Cell(dataRow, 1).Value = row.Rank;
            ws.Cell(dataRow, 2).Value = row.Code;
            ws.Cell(dataRow, 3).Value = row.Name;
            ws.Cell(dataRow, 4).Value = row.Value;
            ws.Cell(dataRow, 5).Value = row.Percentage;
            ws.Cell(dataRow, 6).Value = row.CumulativePercentage;
            ws.Cell(dataRow, 7).Value = row.Classification;

            var rowRange = ws.Range(dataRow, 1, dataRow, 7);
            rowRange.Style.Fill.BackgroundColor = bgColor;

            ws.Cell(dataRow, 4).Style.NumberFormat.Format = filter.Metric == ParetoMetric.Quantity ? "#,##0" : "#,##0.00";
            ws.Cell(dataRow, 5).Style.NumberFormat.Format = "0.00";
            ws.Cell(dataRow, 6).Style.NumberFormat.Format = "0.0";
            ws.Cell(dataRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            dataRow++;
        }

        var totalRange = ws.Range(dataRow, 1, dataRow, 7);
        ws.Cell(dataRow, 1).Value = "";
        ws.Cell(dataRow, 2).Value = "";
        ws.Cell(dataRow, 3).Value = "TOTAL";
        ws.Cell(dataRow, 4).Value = result.GrandTotal;
        ws.Cell(dataRow, 5).Value = 100;
        ws.Cell(dataRow, 6).Value = "";
        ws.Cell(dataRow, 7).Value = "";
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Cell(dataRow, 4).Style.NumberFormat.Format = filter.Metric == ParetoMetric.Quantity ? "#,##0" : "#,##0.00";

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateCancelLogExcel(
        List<CancelLogDetailedRow>? detailedRows,
        List<CancelLogSummaryRow>? summaryRows,
        CancelLogFilter filter)
    {
        bool isDetailed = filter.ReportType == CancelLogReportType.Detailed;
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Cancel Log");

        ws.Cell(1, 1).Value = "Cancellation Logging Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Report Type: {filter.ReportType}";
        ws.Cell(selRow++, 1).Value = $"Action Type: {filter.ActionType}";
        if (hasL1) ws.Cell(selRow++, 1).Value = $"Primary Group: {filter.PrimaryGroup}";
        if (hasL2) ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroup}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;

        if (isDetailed)
        {
            ws.Cell(headerRow, col++).Value = "Store/Station";
            if (hasL1) ws.Cell(headerRow, col++).Value = "Group 1";
            if (hasL2) ws.Cell(headerRow, col++).Value = "Group 2";
            ws.Cell(headerRow, col++).Value = "Action";
            ws.Cell(headerRow, col++).Value = "Session Date";
            ws.Cell(headerRow, col++).Value = "Trans Kind";
            ws.Cell(headerRow, col++).Value = "Customer";
            ws.Cell(headerRow, col++).Value = "Item Code";
            ws.Cell(headerRow, col++).Value = "Item Descr";
            ws.Cell(headerRow, col++).Value = "User";
            ws.Cell(headerRow, col++).Value = "Invoice/Credit";
            ws.Cell(headerRow, col++).Value = "Z Report";
            ws.Cell(headerRow, col++).Value = "Total Lines";
            ws.Cell(headerRow, col++).Value = "Invoice Total";
            ws.Cell(headerRow, col++).Value = "Quantity";
            ws.Cell(headerRow, col++).Value = "Amount";
            ws.Cell(headerRow, col++).Value = "Table No";
            ws.Cell(headerRow, col++).Value = "Table Name";
            ws.Cell(headerRow, col++).Value = "Compartment";
        }
        else
        {
            ws.Cell(headerRow, col++).Value = "Store/Station";
            if (hasL1) ws.Cell(headerRow, col++).Value = "Group 1";
            if (hasL2) ws.Cell(headerRow, col++).Value = "Group 2";
            ws.Cell(headerRow, col++).Value = "Deleted";
            ws.Cell(headerRow, col++).Value = "Cancelled";
            ws.Cell(headerRow, col++).Value = "Complimentary";
            ws.Cell(headerRow, col++).Value = "Invoice Total";
            ws.Cell(headerRow, col++).Value = "Quantity";
            ws.Cell(headerRow, col++).Value = "Amount";
        }

        int totalCols = col - 1;
        var headerRange = ws.Range(headerRow, 1, headerRow, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;

        if (isDetailed)
        {
            foreach (var row in detailedRows ?? new())
            {
                col = 1;
                ws.Cell(dataRow, col++).Value = row.StoreAndStation;
                if (hasL1) ws.Cell(dataRow, col++).Value = row.Level1Descr;
                if (hasL2) ws.Cell(dataRow, col++).Value = row.Level2Descr;
                ws.Cell(dataRow, col++).Value = row.ActionType;
                if (row.SessionDateTime.HasValue)
                    ws.Cell(dataRow, col).Value = row.SessionDateTime.Value;
                col++;
                ws.Cell(dataRow, col++).Value = row.TransKind == "I" ? "Sale" : "Return";
                ws.Cell(dataRow, col++).Value = row.CustomerFullName;
                ws.Cell(dataRow, col++).Value = row.ItemCode;
                ws.Cell(dataRow, col++).Value = row.ItemDescr;
                ws.Cell(dataRow, col++).Value = row.UserCode;
                ws.Cell(dataRow, col++).Value = !string.IsNullOrEmpty(row.InvoiceId) ? row.InvoiceId : row.CreditId;
                ws.Cell(dataRow, col++).Value = row.ZReport;
                ws.Cell(dataRow, col++).Value = row.TotalInvoiceLines;
                ws.Cell(dataRow, col).Value = row.InvoiceTotal;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = row.Quantity;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = row.Amount;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col++).Value = row.TableNo;
                ws.Cell(dataRow, col++).Value = row.TableName;
                ws.Cell(dataRow, col++).Value = row.CompartmentName;
                dataRow++;
            }
        }
        else
        {
            foreach (var row in summaryRows ?? new())
            {
                col = 1;
                ws.Cell(dataRow, col++).Value = row.StoreAndStation;
                if (hasL1) ws.Cell(dataRow, col++).Value = row.Level1Descr;
                if (hasL2) ws.Cell(dataRow, col++).Value = row.Level2Descr;
                ws.Cell(dataRow, col++).Value = row.DeletedAction;
                ws.Cell(dataRow, col++).Value = row.CancelledAction;
                ws.Cell(dataRow, col++).Value = row.ComplimentaryAction;
                ws.Cell(dataRow, col).Value = row.InvoiceTotal;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = row.Quantity;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = row.Amount;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                dataRow++;
            }
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

