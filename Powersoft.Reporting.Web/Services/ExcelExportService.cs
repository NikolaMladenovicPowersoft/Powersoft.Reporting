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
        bool hasSecondary = filter.SecondaryGroupBy != Core.Enums.GroupByType.None;
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;
        
        // Headers
        if (hasGrouping) ws.Cell(headerRow, col++).Value = filter.GroupBy.ToString();
        if (hasSecondary) ws.Cell(headerRow, col++).Value = filter.SecondaryGroupBy.ToString();
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
        
        // Data rows. Avg Basket / Avg Qty are non-additive: subtotals recompute them
        // from summed components (Sales / Net Trans.) exactly like the on-screen grid.
        int dataRow = headerRow + 1;
        void Money(decimal v) { ws.Cell(dataRow, col).Value = v; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00"; }
        void Pct(decimal v) { ws.Cell(dataRow, col).Value = v; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.0\"%\""; }

        void WriteRow(AverageBasketRow row)
        {
            col = 1;
            if (hasGrouping) ws.Cell(dataRow, col++).Value = row.Level1Value ?? row.Level1 ?? "N/A";
            if (hasSecondary) ws.Cell(dataRow, col++).Value = row.Level2Value ?? row.Level2 ?? "N/A";
            ws.Cell(dataRow, col++).Value = row.Period;
            ws.Cell(dataRow, col++).Value = row.CYInvoiceCount;
            ws.Cell(dataRow, col++).Value = row.CYCreditCount;
            ws.Cell(dataRow, col++).Value = row.CYTotalTransactions;
            ws.Cell(dataRow, col++).Value = row.CYQtySold;
            ws.Cell(dataRow, col++).Value = row.CYQtyReturned;
            ws.Cell(dataRow, col++).Value = row.CYTotalQty;
            Money(includeVat ? row.CYTotalGross : row.CYTotalNet);
            Money(includeVat ? row.CYAverageGross : row.CYAverageNet);
            Money(row.CYAverageQty);
            if (compareLY)
            {
                Money(includeVat ? row.LYTotalGross : row.LYTotalNet);
                Money(includeVat ? row.LYAverageGross : row.LYAverageNet);
                Pct(row.YoYChangePercent);
            }
            dataRow++;
        }

        void WriteSub(List<AverageBasketRow> g)
        {
            col = 1;
            if (hasGrouping) ws.Cell(dataRow, col++).Value = "Subtotal";
            if (hasSecondary) ws.Cell(dataRow, col++).Value = "";
            ws.Cell(dataRow, col++).Value = "";
            var gInv = g.Sum(r => r.CYInvoiceCount);
            var gCred = g.Sum(r => r.CYCreditCount);
            var gTrans = g.Sum(r => r.CYTotalTransactions);
            var gQtySold = g.Sum(r => r.CYQtySold);
            var gQtyRet = g.Sum(r => r.CYQtyReturned);
            var gNetQty = g.Sum(r => r.CYTotalQty);
            var gSales = g.Sum(r => includeVat ? r.CYTotalGross : r.CYTotalNet);
            var gAvgBasket = gTrans > 0 ? gSales / gTrans : 0m;
            var gAvgQty = gTrans > 0 ? (decimal)gNetQty / gTrans : 0m;
            ws.Cell(dataRow, col++).Value = gInv;
            ws.Cell(dataRow, col++).Value = gCred;
            ws.Cell(dataRow, col++).Value = gTrans;
            ws.Cell(dataRow, col++).Value = gQtySold;
            ws.Cell(dataRow, col++).Value = gQtyRet;
            ws.Cell(dataRow, col++).Value = gNetQty;
            Money(gSales);
            Money(gAvgBasket);
            Money(gAvgQty);
            if (compareLY)
            {
                var gLyTrans = g.Sum(r => r.LYTotalTransactions);
                var gLySales = g.Sum(r => includeVat ? r.LYTotalGross : r.LYTotalNet);
                var gLyAvg = gLyTrans > 0 ? gLySales / gLyTrans : 0m;
                var gYoY = gLySales != 0 ? Math.Round((gSales - gLySales) / Math.Abs(gLySales) * 100, 2) : (gSales > 0 ? 100m : 0m);
                Money(gLySales);
                Money(gLyAvg);
                Pct(gYoY);
            }
            var rng = ws.Range(dataRow, 1, dataRow, col - 1);
            rng.Style.Font.Bold = true;
            rng.Style.Fill.BackgroundColor = XLColor.FromHtml("#eef2ff");
            dataRow++;
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
        
        // Grand total row
        if (grandTotals != null)
        {
            col = 1;
            if (hasGrouping) ws.Cell(dataRow, col++).Value = "";
            if (hasSecondary) ws.Cell(dataRow, col++).Value = "";
            ws.Cell(dataRow, col++).Value = "GRAND TOTAL";
            ws.Cell(dataRow, col++).Value = grandTotals.TotalInvoices;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalCredits;
            ws.Cell(dataRow, col++).Value = grandTotals.NetTransactions;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalQtySold;
            ws.Cell(dataRow, col++).Value = grandTotals.TotalQtyReturned;
            ws.Cell(dataRow, col++).Value = grandTotals.NetQty;
            Money(includeVat ? grandTotals.GrossSales : grandTotals.NetSales);
            Money(includeVat ? grandTotals.AverageBasketGross : grandTotals.AverageBasketNet);
            Money(grandTotals.AverageQty);
            if (compareLY)
            {
                Money(includeVat ? grandTotals.LYTotalGross : grandTotals.LYTotalNet);
                Money(includeVat ? grandTotals.LYAverageBasketGross : grandTotals.LYAverageBasketNet);
                Pct(grandTotals.YoYChangePercent);
            }
            
            var totalRange = ws.Range(dataRow, 1, dataRow, col - 1);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
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
        if (filter.ShowOnOrder) ws.Cell(selRow++, 1).Value = "Show On Order: Yes";
        if (filter.ShowReservation) ws.Cell(selRow++, 1).Value = "Show Reservation: Yes";
        if (filter.ShowAvailable) ws.Cell(selRow++, 1).Value = "Show Available: Yes";
        if (!filter.IncludeAdditionalCharges) ws.Cell(selRow++, 1).Value = "Cost: Wholesale only (excl. additional charges)";
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
        if (filter.ShowOnOrder) ws.Cell(headerRow, col++).Value = "On Order";
        if (filter.ShowReservation) ws.Cell(headerRow, col++).Value = "Reserved";
        if (filter.ShowAvailable) ws.Cell(headerRow, col++).Value = "Available";

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
            if (filter.ShowOnOrder) ws.Cell(dataRow, col++).Value = row.QtyOnOrder;
            if (filter.ShowReservation) ws.Cell(dataRow, col++).Value = row.QtyReserved;
            if (filter.ShowAvailable) ws.Cell(dataRow, col++).Value = row.QtyAvailable;
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
            if (filter.ShowOnOrder) ws.Cell(dataRow, col++).Value = totals.TotalQtyOnOrder;
            if (filter.ShowReservation) ws.Cell(dataRow, col++).Value = totals.TotalQtyReserved;
            if (filter.ShowAvailable) ws.Cell(dataRow, col++).Value = totals.TotalQtyAvailable;

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
        CatalogueFilter filter,
        bool viewCost = true,
        bool viewSupplier = true)
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
        if (dc("Profit")    && viewCost) cols.Add(("Profit", "Profit", true, r => r.ProfitValue));
        if (dc("Markup")    && viewCost) cols.Add(("Markup", "Markup %", true, r => r.Markup));
        if (dc("Margin")    && viewCost) cols.Add(("Margin", "Margin %", true, r => r.Margin));
        if (dc("Cost")      && viewCost) cols.Add(("Cost", "Cost", true, r => r.Cost));
        if (dc("TotalCost") && viewCost) cols.Add(("TotalCost", "Total Cost", true, r => r.TotalCost));
        if (dc("TotalStockQty")) cols.Add(("TotalStockQty", "Stock Qty", true, r => r.TotalStockQty));
        if (dc("TotalStockValue")) cols.Add(("TotalStockValue", "Stock Value", true, r => r.TotalStockValue));
        if (dc("EntityCode")) cols.Add(("EntityCode", "Entity Code", false, r => r.EntityCode));
        if (dc("EntityName")) cols.Add(("EntityName", "Entity Name", false, r => r.EntityName));
        if (dc("EntityTel1")) cols.Add(("EntityTel1", "Phone", false, r => r.EntityTel1));
        if (dc("EntityTel2")) cols.Add(("EntityTel2", "Phone 2", false, r => r.EntityTel2));
        if (dc("EntityMobile")) cols.Add(("EntityMobile", "Mobile", false, r => r.EntityMobile));
        if (dc("EntityFax")) cols.Add(("EntityFax", "Fax", false, r => r.EntityFax));
        if (dc("EntityEmail")) cols.Add(("EntityEmail", "Email", false, r => r.EntityEmail));
        if (dc("EntityContactName")) cols.Add(("EntityContactName", "Contact", false, r => r.EntityContactName));
        if (dc("EntityVatRegNo")) cols.Add(("EntityVatRegNo", "VAT Reg No", false, r => r.EntityVatRegNo));
        if (dc("EntityDOB")) cols.Add(("EntityDOB", "Date of Birth", false, r => r.EntityDOB?.ToString("yyyy-MM-dd") ?? ""));
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
        if (dc("ItemSupplier") && viewSupplier) cols.Add(("ItemSupplier", "Supplier", false, r => r.ItemSupplierName));
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

        void WriteDataRow(CatalogueRow row)
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

        // Subtotal row: group identity already lives in the _L1/_L2/_L3 columns, so (like
        // PurchasesSales) we emit subtotal rows only — no banner header rows. Math is delegated to
        // CatalogueSubtotal so the numbers are identical to the grid/preview.
        void WriteSubtotal(IEnumerable<CatalogueRow> grp, string label, int level)
        {
            var st = CatalogueSubtotal.From(grp);
            int labelCol = cols.FindIndex(c => c.Key == "_L" + level);
            string bg = level == 1 ? "#dbeafe" : level == 2 ? "#e8f0fe" : "#f0f4f8";
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i].IsNumeric)
                {
                    var v = st.ValueForKey(cols[i].Key);
                    if (v.HasValue)
                    {
                        ws.Cell(dataRow, i + 1).Value = v.Value;
                        ws.Cell(dataRow, i + 1).Style.NumberFormat.Format = "#,##0.00";
                    }
                }
                else if (i == labelCol)
                {
                    ws.Cell(dataRow, i + 1).Value = label;
                }
            }
            var r = ws.Range(dataRow, 1, dataRow, cols.Count);
            r.Style.Font.Bold = true;
            r.Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
            dataRow++;
        }

        if (hasL1)
        {
            foreach (var l1 in rows.GroupBy(r => r.Level1Value ?? r.Level1 ?? "N/A"))
            {
                var l1Rows = l1.ToList();
                if (hasL2)
                {
                    foreach (var l2 in l1Rows.GroupBy(r => r.Level2Value ?? "N/A"))
                    {
                        var l2Rows = l2.ToList();
                        if (hasL3)
                        {
                            foreach (var l3 in l2Rows.GroupBy(r => r.Level3Value ?? "N/A"))
                            {
                                var l3Rows = l3.ToList();
                                foreach (var row in l3Rows) WriteDataRow(row);
                                WriteSubtotal(l3Rows, l3.Key + " subtotal", 3);
                            }
                        }
                        else
                        {
                            foreach (var row in l2Rows) WriteDataRow(row);
                        }
                        WriteSubtotal(l2Rows, l2.Key + " subtotal", 2);
                    }
                }
                else
                {
                    foreach (var row in l1Rows) WriteDataRow(row);
                }
                WriteSubtotal(l1Rows, l1.Key + " total", 1);
            }
        }
        else
        {
            foreach (var row in rows) WriteDataRow(row);
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

    public byte[] GenerateParetoExcel(ParetoResult result, ParetoFilter filter, bool viewCost = true)
    {
        // Column offset applied to all columns after the (optional) Profit column.
        // When cost is hidden the Profit column is dropped and later columns shift left by 1.
        int po = viewCost ? 0 : -1;
        int lastCol = 10 + po;

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
        ws.Cell(headerRow, 4).Value = "Quantity";
        ws.Cell(headerRow, 5).Value = "Subtotal";
        if (viewCost) ws.Cell(headerRow, 6).Value = "Profit";
        ws.Cell(headerRow, 7 + po).Value = "%";
        ws.Cell(headerRow, 8 + po).Value = "Cumul. %";
        ws.Cell(headerRow, 9 + po).Value = "Class";
        ws.Cell(headerRow, 10 + po).Value = "Display";

        var headerRange = ws.Range(headerRow, 1, headerRow, lastCol);
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
            ws.Cell(dataRow, 4).Value = row.Quantity;
            ws.Cell(dataRow, 5).Value = row.Subtotal;
            if (viewCost)
            {
                ws.Cell(dataRow, 6).Value = row.Profit;
                ws.Cell(dataRow, 6).Style.NumberFormat.Format = "#,##0.00";
            }
            ws.Cell(dataRow, 7 + po).Value = row.Percentage;
            ws.Cell(dataRow, 8 + po).Value = row.CumulativePercentage;
            ws.Cell(dataRow, 9 + po).Value = row.Classification;
            ws.Cell(dataRow, 10 + po).Value = row.IsDisplay ? "Yes" : "";

            var rowRange = ws.Range(dataRow, 1, dataRow, lastCol);
            rowRange.Style.Fill.BackgroundColor = bgColor;

            ws.Cell(dataRow, 4).Style.NumberFormat.Format = "#,##0.####";
            ws.Cell(dataRow, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, 7 + po).Style.NumberFormat.Format = "0.00";
            ws.Cell(dataRow, 8 + po).Style.NumberFormat.Format = "0.0";
            ws.Cell(dataRow, 9 + po).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            dataRow++;
        }

        var totalRange = ws.Range(dataRow, 1, dataRow, lastCol);
        ws.Cell(dataRow, 1).Value = "";
        ws.Cell(dataRow, 2).Value = "";
        ws.Cell(dataRow, 3).Value = "TOTAL";
        ws.Cell(dataRow, 4).Value = result.TotalQuantity;
        ws.Cell(dataRow, 5).Value = result.TotalSubtotal;
        if (viewCost)
        {
            ws.Cell(dataRow, 6).Value = result.TotalProfit;
            ws.Cell(dataRow, 6).Style.NumberFormat.Format = "#,##0.00";
        }
        ws.Cell(dataRow, 7 + po).Value = 100;
        ws.Cell(dataRow, 8 + po).Value = "";
        ws.Cell(dataRow, 9 + po).Value = "";
        ws.Cell(dataRow, 10 + po).Value = "";
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Cell(dataRow, 4).Style.NumberFormat.Format = "#,##0.####";
        ws.Cell(dataRow, 5).Style.NumberFormat.Format = "#,##0.00";

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
        int leading = 1 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);

        void StyleSubtotal(int level)
        {
            var rng = ws.Range(dataRow, 1, dataRow, totalCols);
            rng.Style.Font.Bold = true;
            rng.Style.Fill.BackgroundColor = XLColor.FromHtml(level == 1 ? "#dbeafe" : "#eef2ff");
        }

        if (isDetailed)
        {
            void WriteRow(CancelLogDetailedRow row)
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

            void WriteSub(List<CancelLogDetailedRow> g, string label, int level)
            {
                ws.Cell(dataRow, 1).Value = label;
                ws.Cell(dataRow, leading + 11).Value = g.Sum(x => x.InvoiceTotal);
                ws.Cell(dataRow, leading + 11).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, leading + 12).Value = g.Sum(x => x.Quantity);
                ws.Cell(dataRow, leading + 12).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, leading + 13).Value = g.Sum(x => x.Amount);
                ws.Cell(dataRow, leading + 13).Style.NumberFormat.Format = "#,##0.00";
                StyleSubtotal(level);
                dataRow++;
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
                            WriteSub(l2Rows, l2.Key + " subtotal", 2);
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteRow(row);
                    }
                    WriteSub(l1Rows, l1.Key + " total", 1);
                }
            }
            else
            {
                foreach (var row in det) WriteRow(row);
            }
        }
        else
        {
            void WriteRow(CancelLogSummaryRow row)
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

            void WriteSub(List<CancelLogSummaryRow> g, string label, int level)
            {
                ws.Cell(dataRow, 1).Value = label;
                ws.Cell(dataRow, leading + 1).Value = g.Sum(x => x.DeletedAction);
                ws.Cell(dataRow, leading + 2).Value = g.Sum(x => x.CancelledAction);
                ws.Cell(dataRow, leading + 3).Value = g.Sum(x => x.ComplimentaryAction);
                ws.Cell(dataRow, leading + 4).Value = g.Sum(x => x.InvoiceTotal);
                ws.Cell(dataRow, leading + 4).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, leading + 5).Value = g.Sum(x => x.Quantity);
                ws.Cell(dataRow, leading + 5).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, leading + 6).Value = g.Sum(x => x.Amount);
                ws.Cell(dataRow, leading + 6).Style.NumberFormat.Format = "#,##0.00";
                StyleSubtotal(level);
                dataRow++;
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
                            WriteSub(l2Rows, l2.Key + " subtotal", 2);
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteRow(row);
                    }
                    WriteSub(l1Rows, l1.Key + " total", 1);
                }
            }
            else
            {
                foreach (var row in sum) WriteRow(row);
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
        if (filter.ShowOnOrder) ws.Cell(dataRow, col++).Value = agg.OnOrderQty;
        if (filter.ShowReservation) ws.Cell(dataRow, col++).Value = agg.ReservedQty;
        if (filter.ShowAvailable) ws.Cell(dataRow, col++).Value = agg.AvailableQty;

        var range = ws.Range(dataRow, 1, dataRow, totalCols);
        range.Style.Font.Bold = true;
        range.Style.Font.Italic = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
        return dataRow + 1;
    }

    public byte[] GenerateProspectClientsExcel(
        List<ProspectClientsRow> rows, ProspectClientsFilter filter)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Prospect Clients");

        ws.Cell(1, 1).Value = "Prospect Clients Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Date Field: {filter.DateField}";
        if (filter.StatusFilter != "All") ws.Cell(selRow++, 1).Value = $"Status: {filter.StatusFilter}";
        if (filter.PriorityFilter != "All") ws.Cell(selRow++, 1).Value = $"Priority: {filter.PriorityFilter}";
        if (hasL1) ws.Cell(selRow++, 1).Value = $"Primary Group: {filter.PrimaryGroup}";
        if (hasL2) ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroup}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;

        if (hasL1) ws.Cell(headerRow, col++).Value = "Group 1";
        if (hasL2) ws.Cell(headerRow, col++).Value = "Group 2";
        ws.Cell(headerRow, col++).Value = "Lead No";
        ws.Cell(headerRow, col++).Value = "Company/Name";
        ws.Cell(headerRow, col++).Value = "Contact Person";
        ws.Cell(headerRow, col++).Value = "Status";
        ws.Cell(headerRow, col++).Value = "Priority";
        ws.Cell(headerRow, col++).Value = "Registration Date";
        ws.Cell(headerRow, col++).Value = "Last Modified";
        ws.Cell(headerRow, col++).Value = "Next Communication";
        ws.Cell(headerRow, col++).Value = "Phone";
        ws.Cell(headerRow, col++).Value = "Mobile";
        ws.Cell(headerRow, col++).Value = "Email";
        ws.Cell(headerRow, col++).Value = "Town";
        ws.Cell(headerRow, col++).Value = "Followed By";
        ws.Cell(headerRow, col++).Value = "Recommended By";
        ws.Cell(headerRow, col++).Value = "Linked Customer";
        ws.Cell(headerRow, col++).Value = "Category 1";
        ws.Cell(headerRow, col++).Value = "Category 2";
        ws.Cell(headerRow, col++).Value = "Notes";
        ws.Cell(headerRow, col++).Value = "Offers";
        ws.Cell(headerRow, col++).Value = "Offer Value";
        ws.Cell(headerRow, col++).Value = "Emails Sent";
        ws.Cell(headerRow, col++).Value = "SMS Sent";
        if (filter.IncludeHistory) ws.Cell(headerRow, col++).Value = "Source";

        int totalCols = col - 1;
        var headerRange = ws.Range(headerRow, 1, headerRow, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        int leading = 18 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);

        void WriteRow(ProspectClientsRow row)
        {
            col = 1;
            if (hasL1) ws.Cell(dataRow, col++).Value = row.Level1Descr;
            if (hasL2) ws.Cell(dataRow, col++).Value = row.Level2Descr;
            ws.Cell(dataRow, col++).Value = row.LeadNo;
            ws.Cell(dataRow, col++).Value = row.CompanyName;
            ws.Cell(dataRow, col++).Value = row.ContactPerson;
            ws.Cell(dataRow, col++).Value = row.StatusName;
            ws.Cell(dataRow, col++).Value = row.PriorityName;
            if (row.RegistrationDate.HasValue) ws.Cell(dataRow, col).Value = row.RegistrationDate.Value;
            ws.Cell(dataRow, col++).Style.DateFormat.Format = "yyyy-MM-dd";
            if (row.LastModification.HasValue) ws.Cell(dataRow, col).Value = row.LastModification.Value;
            ws.Cell(dataRow, col++).Style.DateFormat.Format = "yyyy-MM-dd";
            if (row.NextCommunicationDate.HasValue) ws.Cell(dataRow, col).Value = row.NextCommunicationDate.Value;
            ws.Cell(dataRow, col++).Style.DateFormat.Format = "yyyy-MM-dd";
            ws.Cell(dataRow, col++).Value = row.Tel1;
            ws.Cell(dataRow, col++).Value = row.Mobile;
            ws.Cell(dataRow, col++).Value = row.Email;
            ws.Cell(dataRow, col++).Value = row.Town;
            ws.Cell(dataRow, col++).Value = row.FollowedBy;
            ws.Cell(dataRow, col++).Value = row.RecommendedBy;
            ws.Cell(dataRow, col++).Value = row.LinkedCustomer;
            ws.Cell(dataRow, col++).Value = row.Category1;
            ws.Cell(dataRow, col++).Value = row.Category2;
            ws.Cell(dataRow, col++).Value = row.Notes;
            ws.Cell(dataRow, col++).Value = row.OfferCount;
            ws.Cell(dataRow, col).Value = row.TotalOfferValue;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col++).Value = row.EmailsSent;
            ws.Cell(dataRow, col++).Value = row.SmsSent;
            if (filter.IncludeHistory) ws.Cell(dataRow, col++).Value = row.Source;
            dataRow++;
        }

        void WriteSub(List<ProspectClientsRow> grp, string label, int level)
        {
            ws.Cell(dataRow, 1).Value = label;
            ws.Cell(dataRow, leading + 1).Value = grp.Sum(x => x.OfferCount);
            ws.Cell(dataRow, leading + 2).Value = grp.Sum(x => x.TotalOfferValue);
            ws.Cell(dataRow, leading + 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, leading + 3).Value = grp.Sum(x => x.EmailsSent);
            ws.Cell(dataRow, leading + 4).Value = grp.Sum(x => x.SmsSent);
            var rng = ws.Range(dataRow, 1, dataRow, totalCols);
            rng.Style.Font.Bold = true;
            rng.Style.Fill.BackgroundColor = XLColor.FromHtml(level == 1 ? "#dbeafe" : "#eef2ff");
            dataRow++;
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
                        WriteSub(l2Rows, l2.Key + " subtotal", 2);
                    }
                }
                else
                {
                    foreach (var row in l1Rows) WriteRow(row);
                }
                WriteSub(l1Rows, l1.Key + " total", 1);
            }
        }
        else
        {
            foreach (var row in rows) WriteRow(row);
        }

        ws.Columns().AdjustToContents(1, 80);
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateOffersReportExcel(
        List<OffersReportRow> rows, OffersReportFilter filter, bool viewCost = true)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";
        bool hasL3 = filter.ThirdGroup != "NONE";

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Offers Report");

        ws.Cell(1, 1).Value = "Offers Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}";
        ws.Cell(selRow++, 1).Value = $"Date Field: {filter.DateField}";
        if (filter.StatusFilter != "All") ws.Cell(selRow++, 1).Value = $"Status: {filter.StatusFilter}";
        if (filter.StoreFilter != "All") ws.Cell(selRow++, 1).Value = $"Store: {filter.StoreFilter}";
        if (hasL1) ws.Cell(selRow++, 1).Value = $"Primary Group: {filter.PrimaryGroup}";
        if (hasL2) ws.Cell(selRow++, 1).Value = $"Secondary Group: {filter.SecondaryGroup}";
        if (hasL3) ws.Cell(selRow++, 1).Value = $"Third Group: {filter.ThirdGroup}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;

        if (hasL1) ws.Cell(headerRow, col++).Value = "Group 1";
        if (hasL2) ws.Cell(headerRow, col++).Value = "Group 2";
        if (hasL3) ws.Cell(headerRow, col++).Value = "Group 3";
        ws.Cell(headerRow, col++).Value = "Offer No";
        ws.Cell(headerRow, col++).Value = "Date";
        ws.Cell(headerRow, col++).Value = "Valid Until";
        ws.Cell(headerRow, col++).Value = "Status";
        ws.Cell(headerRow, col++).Value = "Customer";
        ws.Cell(headerRow, col++).Value = "Store";
        ws.Cell(headerRow, col++).Value = "Agent";
        ws.Cell(headerRow, col++).Value = "Items";
        ws.Cell(headerRow, col++).Value = "Qty";
        ws.Cell(headerRow, col++).Value = "Subtotal";
        ws.Cell(headerRow, col++).Value = "Discount";
        ws.Cell(headerRow, col++).Value = "Disc %";
        ws.Cell(headerRow, col++).Value = "VAT";
        ws.Cell(headerRow, col++).Value = "Grand Total";
        if (viewCost) ws.Cell(headerRow, col++).Value = "Cost";
        ws.Cell(headerRow, col++).Value = "Order %";
        ws.Cell(headerRow, col++).Value = "Lead";
        ws.Cell(headerRow, col++).Value = "Printed";
        ws.Cell(headerRow, col++).Value = "Emailed";
        ws.Cell(headerRow, col++).Value = "Comments";
        ws.Cell(headerRow, col++).Value = "Internal Notes";
        ws.Cell(headerRow, col++).Value = "Source";

        int totalCols = col - 1;
        var headerRange = ws.Range(headerRow, 1, headerRow, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        int leading = 7 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0);

        void WriteRow(OffersReportRow row)
        {
            col = 1;
            if (hasL1) ws.Cell(dataRow, col++).Value = row.Level1Descr;
            if (hasL2) ws.Cell(dataRow, col++).Value = row.Level2Descr;
            if (hasL3) ws.Cell(dataRow, col++).Value = row.Level3Descr;
            ws.Cell(dataRow, col++).Value = row.OfferNo;
            if (row.DateTrans.HasValue) ws.Cell(dataRow, col).Value = row.DateTrans.Value;
            ws.Cell(dataRow, col++).Style.DateFormat.Format = "yyyy-MM-dd";
            if (row.ValidUntil.HasValue) ws.Cell(dataRow, col).Value = row.ValidUntil.Value;
            ws.Cell(dataRow, col++).Style.DateFormat.Format = "yyyy-MM-dd";
            ws.Cell(dataRow, col++).Value = row.StatusName;
            ws.Cell(dataRow, col++).Value = row.CustomerName;
            ws.Cell(dataRow, col++).Value = row.StoreName;
            ws.Cell(dataRow, col++).Value = row.AgentName;
            ws.Cell(dataRow, col++).Value = row.ItemCount;
            ws.Cell(dataRow, col).Value = row.TotalQuantity;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col).Value = row.InvoiceTotal;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col).Value = row.InvoiceTotalDiscount;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col).Value = row.InvoiceDiscountPerc;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col).Value = row.InvoiceVat;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col).Value = row.InvoiceGrandTotal;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            if (viewCost)
            {
                ws.Cell(dataRow, col).Value = row.TotalItemCost;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            }
            ws.Cell(dataRow, col).Value = row.OrderPercentage;
            ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, col++).Value = row.LinkedLead;
            ws.Cell(dataRow, col++).Value = row.Printed ? "Yes" : "";
            ws.Cell(dataRow, col++).Value = row.SentByEmail ? "Yes" : "";
            ws.Cell(dataRow, col++).Value = row.Comments;
            ws.Cell(dataRow, col++).Value = row.InternalNotes;
            ws.Cell(dataRow, col++).Value = row.Source;
            dataRow++;
        }

        // Additive numeric columns summed; percentages (Disc %, Order %) left blank — matches grid.
        void WriteSub(List<OffersReportRow> g, string label, int level)
        {
            ws.Cell(dataRow, 1).Value = label;
            void Money(int offset, decimal v)
            {
                ws.Cell(dataRow, leading + offset).Value = v;
                ws.Cell(dataRow, leading + offset).Style.NumberFormat.Format = "#,##0.00";
            }
            ws.Cell(dataRow, leading + 1).Value = g.Sum(x => x.ItemCount);
            Money(2, g.Sum(x => x.TotalQuantity));
            Money(3, g.Sum(x => x.InvoiceTotal));
            Money(4, g.Sum(x => x.InvoiceTotalDiscount));
            // offset 5 = Disc % — blank
            Money(6, g.Sum(x => x.InvoiceVat));
            Money(7, g.Sum(x => x.InvoiceGrandTotal));
            if (viewCost) Money(8, g.Sum(x => x.TotalItemCost));
            var rng = ws.Range(dataRow, 1, dataRow, totalCols);
            rng.Style.Font.Bold = true;
            rng.Style.Fill.BackgroundColor = XLColor.FromHtml(level == 1 ? "#ede9fe" : level == 2 ? "#f3effe" : "#f8f5ff");
            dataRow++;
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
                                WriteSub(l3Rows, l3.Key + " subtotal", 3);
                            }
                        }
                        else
                        {
                            foreach (var row in l2Rows) WriteRow(row);
                        }
                        WriteSub(l2Rows, l2.Key + " subtotal", 2);
                    }
                }
                else
                {
                    foreach (var row in l1Rows) WriteRow(row);
                }
                WriteSub(l1Rows, l1.Key + " total", 1);
            }
        }
        else
        {
            foreach (var row in rows) WriteRow(row);
        }

        ws.Columns().AdjustToContents(1, 80);
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateTrialBalanceExcel(List<TrialBalanceRow> rows, TrialBalanceFilter filter)
    {
        rows ??= new();
        bool isSummary = filter.ReportMode == TrialBalanceReportMode.Summary;

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Trial Balance");

        ws.Cell(1, 1).Value = "Trial Balance";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"As At: {filter.AsAt:dd/MM/yyyy}";
        ws.Cell(selRow++, 1).Value = $"Report Mode: {filter.ReportMode}";
        ws.Cell(selRow++, 1).Value = $"Include Zero Movements: {(filter.IncludeZeroMovements ? "Yes" : "No")}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;
        ws.Cell(headerRow, col++).Value = "Header Code";
        ws.Cell(headerRow, col++).Value = "Header";
        if (!isSummary)
        {
            ws.Cell(headerRow, col++).Value = "Account Code";
            ws.Cell(headerRow, col++).Value = "Account";
        }
        ws.Cell(headerRow, col++).Value = "Opening DR";
        ws.Cell(headerRow, col++).Value = "Opening CR";
        ws.Cell(headerRow, col++).Value = "Debit";
        ws.Cell(headerRow, col++).Value = "Credit";
        ws.Cell(headerRow, col++).Value = "Closing DR";
        ws.Cell(headerRow, col++).Value = "Closing CR";

        int totalCols = col - 1;
        var headerRange = ws.Range(headerRow, 1, headerRow, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        int firstNumCol = isSummary ? 3 : 5;

        void PutNum(int r, int c, decimal v)
        {
            if (v != 0)
            {
                ws.Cell(r, c).Value = v;
                ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
            }
        }

        if (isSummary)
        {
            foreach (var g in rows.GroupBy(r => new { r.HeaderKey, r.HeaderCode, r.HeaderName })
                                   .OrderBy(g => g.First().HeaderCodeSort, StringComparer.Ordinal))
            {
                var list = g.ToList();
                ws.Cell(dataRow, 1).Value = g.Key.HeaderCode;
                ws.Cell(dataRow, 2).Value = g.Key.HeaderName;
                PutNum(dataRow, 3, list.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance));
                PutNum(dataRow, 4, list.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance));
                PutNum(dataRow, 5, list.Sum(r => r.DebitMovement));
                PutNum(dataRow, 6, list.Sum(r => r.CreditMovement));
                PutNum(dataRow, 7, list.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance));
                PutNum(dataRow, 8, list.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance));
                dataRow++;
            }
        }
        else
        {
            foreach (var row in rows)
            {
                ws.Cell(dataRow, 1).Value = row.HeaderCode;
                ws.Cell(dataRow, 2).Value = row.HeaderName;
                ws.Cell(dataRow, 3).Value = row.AccountCode;
                ws.Cell(dataRow, 4).Value = row.AccountName;
                PutNum(dataRow, 5, row.OpeningBalanceType == "DR" ? row.OpeningBalance : 0);
                PutNum(dataRow, 6, row.OpeningBalanceType == "CR" ? row.OpeningBalance : 0);
                PutNum(dataRow, 7, row.DebitMovement);
                PutNum(dataRow, 8, row.CreditMovement);
                PutNum(dataRow, 9, row.ClosingBalanceType == "DR" ? row.ClosingBalance : 0);
                PutNum(dataRow, 10, row.ClosingBalanceType == "CR" ? row.ClosingBalance : 0);
                dataRow++;
            }
        }

        // Grand totals row.
        ws.Cell(dataRow, 1).Value = "TOTAL";
        PutNum(dataRow, firstNumCol,     rows.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance));
        PutNum(dataRow, firstNumCol + 1, rows.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance));
        PutNum(dataRow, firstNumCol + 2, rows.Sum(r => r.DebitMovement));
        PutNum(dataRow, firstNumCol + 3, rows.Sum(r => r.CreditMovement));
        PutNum(dataRow, firstNumCol + 4, rows.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance));
        PutNum(dataRow, firstNumCol + 5, rows.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance));
        var totalRange = ws.Range(dataRow, 1, dataRow, totalCols);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");

        ws.Columns().AdjustToContents(1, 80);
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateProfitLossExcel(List<ProfitLossRow> rows, ProfitLossFilter filter)
    {
        rows ??= new();
        bool compare = filter.CompareToLastYear;
        var visible = rows.Where(r => !r.Suppressed).ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Profit & Loss");

        ws.Cell(1, 1).Value = "Profit & Loss";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int selRow = 2;
        ws.Cell(selRow++, 1).Value = $"Period: {filter.DateFrom:dd/MM/yyyy} - {filter.DateTo:dd/MM/yyyy}";
        ws.Cell(selRow++, 1).Value = $"Header Level: {(filter.HeaderLevel ? "Yes" : "No")}";
        if (compare)
            ws.Cell(selRow++, 1).Value = $"Comparison Period: {filter.PriorDateFrom:dd/MM/yyyy} - {filter.PriorDateTo:dd/MM/yyyy}";
        ws.Cell(selRow++, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        for (int sr = 2; sr < selRow; sr++)
            ws.Cell(sr, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

        int headerRow = selRow + 1;
        int col = 1;
        ws.Cell(headerRow, col++).Value = "Group";
        ws.Cell(headerRow, col++).Value = "Account Code";
        ws.Cell(headerRow, col++).Value = "Account";
        ws.Cell(headerRow, col++).Value = "Amount";
        if (compare)
        {
            ws.Cell(headerRow, col++).Value = "Prior Year";
            ws.Cell(headerRow, col++).Value = "Variance";
            ws.Cell(headerRow, col++).Value = "Variance %";
        }
        int totalCols = col - 1;
        var headerRange = ws.Range(headerRow, 1, headerRow, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int dataRow = headerRow + 1;
        void PutNum(int r, int c, decimal v)
        {
            ws.Cell(r, c).Value = v;
            ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
        }

        foreach (var grp in visible.GroupBy(r => r.Group).OrderBy(g => (int)g.Key))
        {
            var list = grp.ToList();
            foreach (var row in list)
            {
                ws.Cell(dataRow, 1).Value = row.GroupName;
                ws.Cell(dataRow, 2).Value = row.AccountCode;
                ws.Cell(dataRow, 3).Value = row.AccountName;
                PutNum(dataRow, 4, row.Balance);
                if (compare)
                {
                    PutNum(dataRow, 5, row.PriorBalance);
                    PutNum(dataRow, 6, row.Variance);
                    if (row.VariancePercent.HasValue) PutNum(dataRow, 7, row.VariancePercent.Value);
                }
                dataRow++;
            }

            ws.Cell(dataRow, 1).Value = list[0].GroupName + " subtotal";
            PutNum(dataRow, 4, list.Sum(r => r.Balance));
            if (compare)
            {
                PutNum(dataRow, 5, list.Sum(r => r.PriorBalance));
                PutNum(dataRow, 6, list.Sum(r => r.Variance));
            }
            ws.Range(dataRow, 1, dataRow, totalCols).Style.Font.Italic = true;
            ws.Range(dataRow, 1, dataRow, totalCols).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
            dataRow++;
        }

        decimal Tot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.Balance);
        decimal PriorTot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.PriorBalance);
        decimal gross = Tot(ProfitLossGroup.Sales) - Tot(ProfitLossGroup.CostOfSales);
        decimal net = gross + Tot(ProfitLossGroup.Income) - Tot(ProfitLossGroup.Expenses);
        decimal pGross = PriorTot(ProfitLossGroup.Sales) - PriorTot(ProfitLossGroup.CostOfSales);
        decimal pNet = pGross + PriorTot(ProfitLossGroup.Income) - PriorTot(ProfitLossGroup.Expenses);

        dataRow++;
        void SummaryRow(string label, decimal cur, decimal prior)
        {
            ws.Cell(dataRow, 1).Value = label;
            PutNum(dataRow, 4, cur);
            if (compare) { PutNum(dataRow, 5, prior); PutNum(dataRow, 6, cur - prior); }
            var rng = ws.Range(dataRow, 1, dataRow, totalCols);
            rng.Style.Font.Bold = true;
            rng.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            dataRow++;
        }
        SummaryRow("GROSS PROFIT", gross, pGross);
        SummaryRow("NET PROFIT", net, pNet);

        ws.Columns().AdjustToContents(1, 80);
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateBelowMinStockExcel(List<BelowMinStockRow> rows, BelowMinStockFilter filter, bool viewCost = true)
    {
        rows ??= new();
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Below Min Stock");

        ws.Cell(1, 1).Value = "Below Minimum Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        ws.Cell(3, 1).Value = $"Total Items: {rows.Count}";

        int headerRow = 5;
        int col = 1;
        var headers = new List<string> { "Item Code", "Item Name", "Store", "Store Name", "Category", "Department", "Brand", "Current Stock", "Minimum Stock", "Difference" };
        if (viewCost) { headers.Add("Cost"); headers.Add("Stock Value"); }
        headers.Add("Shelf");

        foreach (var h in headers)
        {
            ws.Cell(headerRow, col).Value = h;
            ws.Cell(headerRow, col).Style.Font.Bold = true;
            ws.Cell(headerRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a365d");
            ws.Cell(headerRow, col).Style.Font.FontColor = XLColor.White;
            col++;
        }

        int dataRow = headerRow + 1;
        foreach (var r in rows)
        {
            col = 1;
            ws.Cell(dataRow, col++).Value = r.ItemCode;
            ws.Cell(dataRow, col++).Value = r.ItemName;
            ws.Cell(dataRow, col++).Value = r.StoreCode;
            ws.Cell(dataRow, col++).Value = r.StoreName;
            ws.Cell(dataRow, col++).Value = r.CategoryName ?? "";
            ws.Cell(dataRow, col++).Value = r.DepartmentName ?? "";
            ws.Cell(dataRow, col++).Value = r.BrandName ?? "";
            ws.Cell(dataRow, col).Value = r.CurrentStock; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0";
            ws.Cell(dataRow, col).Value = r.MinimumStock; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0";
            ws.Cell(dataRow, col).Value = r.Difference; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0";
            if (viewCost)
            {
                ws.Cell(dataRow, col).Value = r.Cost ?? 0; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = r.StockValue ?? 0; ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
            }
            ws.Cell(dataRow, col++).Value = r.Shelf ?? "";

            if (r.Difference < 0)
            {
                ws.Range(dataRow, 1, dataRow, headers.Count).Style.Font.FontColor = XLColor.Red;
            }
            dataRow++;
        }

        ws.Columns().AdjustToContents(1, 80);
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

