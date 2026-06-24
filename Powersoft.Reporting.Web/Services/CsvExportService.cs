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
        bool hasSecondary = filter.SecondaryGroupBy != Core.Enums.GroupByType.None;
        bool includeVat = filter.IncludeVat;
        bool compareLY = filter.CompareLastYear;

        var sb = new StringBuilder();

        sb.AppendLine($"# Average Basket Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Breakdown: {filter.Breakdown}");
        sb.AppendLine($"# Group By: {filter.GroupBy}");
        if (hasSecondary)
            sb.AppendLine($"# Secondary Group: {filter.SecondaryGroupBy}");
        sb.AppendLine($"# Include VAT: {(includeVat ? "Yes" : "No")}");
        if (compareLY) sb.AppendLine("# Compare Last Year: Yes");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        var headers = new List<string>();
        if (hasGrouping) headers.Add(filter.GroupBy.ToString());
        if (hasSecondary) headers.Add(filter.SecondaryGroupBy.ToString());
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

        string F2(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);

        void WriteRow(AverageBasketRow row)
        {
            var cells = new List<string>();
            if (hasGrouping) cells.Add(row.Level1Value ?? row.Level1 ?? "N/A");
            if (hasSecondary) cells.Add(row.Level2Value ?? row.Level2 ?? "N/A");
            cells.Add(row.Period);
            cells.Add(row.CYInvoiceCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYCreditCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYTotalTransactions.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYQtySold.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYQtyReturned.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CYTotalQty.ToString(CultureInfo.InvariantCulture));
            cells.Add(F2(includeVat ? row.CYTotalGross : row.CYTotalNet));
            cells.Add(F2(includeVat ? row.CYAverageGross : row.CYAverageNet));
            cells.Add(F2(row.CYAverageQty));
            if (compareLY)
            {
                cells.Add(F2(includeVat ? row.LYTotalGross : row.LYTotalNet));
                cells.Add(F2(includeVat ? row.LYAverageGross : row.LYAverageNet));
                cells.Add(row.YoYChangePercent.ToString("F1", CultureInfo.InvariantCulture));
            }
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        // Subtotal: counts/qty/sales summed; Avg Basket / Avg Qty recomputed from sums
        // (Sales / Net Trans.) exactly like the on-screen grid.
        void WriteSub(List<AverageBasketRow> g)
        {
            var cells = new List<string>();
            if (hasGrouping) cells.Add("Subtotal");
            if (hasSecondary) cells.Add("");
            cells.Add("");
            var gInv = g.Sum(r => r.CYInvoiceCount);
            var gCred = g.Sum(r => r.CYCreditCount);
            var gTrans = g.Sum(r => r.CYTotalTransactions);
            var gQtySold = g.Sum(r => r.CYQtySold);
            var gQtyRet = g.Sum(r => r.CYQtyReturned);
            var gNetQty = g.Sum(r => r.CYTotalQty);
            var gSales = g.Sum(r => includeVat ? r.CYTotalGross : r.CYTotalNet);
            var gAvgBasket = gTrans > 0 ? gSales / gTrans : 0m;
            var gAvgQty = gTrans > 0 ? gNetQty / gTrans : 0m;
            cells.Add(gInv.ToString(CultureInfo.InvariantCulture));
            cells.Add(gCred.ToString(CultureInfo.InvariantCulture));
            cells.Add(gTrans.ToString(CultureInfo.InvariantCulture));
            cells.Add(gQtySold.ToString(CultureInfo.InvariantCulture));
            cells.Add(gQtyRet.ToString(CultureInfo.InvariantCulture));
            cells.Add(gNetQty.ToString(CultureInfo.InvariantCulture));
            cells.Add(F2(gSales));
            cells.Add(F2(gAvgBasket));
            cells.Add(F2(gAvgQty));
            if (compareLY)
            {
                var gLyTrans = g.Sum(r => r.LYTotalTransactions);
                var gLySales = g.Sum(r => includeVat ? r.LYTotalGross : r.LYTotalNet);
                var gLyAvg = gLyTrans > 0 ? gLySales / gLyTrans : 0m;
                var gYoY = gLySales != 0 ? Math.Round((gSales - gLySales) / Math.Abs(gLySales) * 100, 2) : (gSales > 0 ? 100m : 0m);
                cells.Add(F2(gLySales));
                cells.Add(F2(gLyAvg));
                cells.Add(gYoY.ToString("F1", CultureInfo.InvariantCulture));
            }
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
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
            var cells = new List<string>();
            if (hasGrouping) cells.Add("");
            if (hasSecondary) cells.Add("");

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
        if (filter.ShowOnOrder) sb.AppendLine("# Show On Order: Yes");
        if (filter.ShowReservation) sb.AppendLine("# Show Reservation: Yes");
        if (filter.ShowAvailable) sb.AppendLine("# Show Available: Yes");
        if (!filter.IncludeAdditionalCharges) sb.AppendLine("# Cost: Wholesale only (excl. additional charges)");
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
        if (filter.ShowOnOrder) headers.Add("On Order");
        if (filter.ShowReservation) headers.Add("Reserved");
        if (filter.ShowAvailable) headers.Add("Available");
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
            if (filter.ShowOnOrder) cells.Add(row.QtyOnOrder.ToString(CultureInfo.InvariantCulture));
            if (filter.ShowReservation) cells.Add(row.QtyReserved.ToString(CultureInfo.InvariantCulture));
            if (filter.ShowAvailable) cells.Add(row.QtyAvailable.ToString(CultureInfo.InvariantCulture));
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
            if (filter.ShowOnOrder) cells.Add(totals.TotalQtyOnOrder.ToString(CultureInfo.InvariantCulture));
            if (filter.ShowReservation) cells.Add(totals.TotalQtyReserved.ToString(CultureInfo.InvariantCulture));
            if (filter.ShowAvailable) cells.Add(totals.TotalQtyAvailable.ToString(CultureInfo.InvariantCulture));
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
        if (filter.ShowOnOrder) cells.Add(agg.OnOrderQty.ToString(CultureInfo.InvariantCulture));
        if (filter.ShowReservation) cells.Add(agg.ReservedQty.ToString(CultureInfo.InvariantCulture));
        if (filter.ShowAvailable) cells.Add(agg.AvailableQty.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join(",", cells.Select(Escape)));
    }

    public byte[] GenerateCatalogueCsv(
        List<CatalogueRow> rows,
        CatalogueTotals? totals,
        CatalogueFilter filter,
        bool viewCost = true,
        bool viewSupplier = true)
    {
        bool hasL1 = filter.PrimaryGroup != Core.Enums.CatalogueGroupBy.None;
        bool hasL2 = filter.SecondaryGroup != Core.Enums.CatalogueGroupBy.None;
        bool hasL3 = filter.ThirdGroup != Core.Enums.CatalogueGroupBy.None;
        bool isSummary = filter.IsSummary;
        bool showItem = !isSummary || (!hasL1 && !hasL2 && !hasL3);
        bool dc(string c) => filter.DisplayColumns.Contains(c, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# Power Reports Catalogue");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Report Mode: {filter.ReportMode}");
        sb.AppendLine($"# Report On: {filter.ReportOn}");
        if (hasL1) sb.AppendLine($"# Primary Group: {filter.PrimaryGroup}");
        if (hasL2) sb.AppendLine($"# Secondary Group: {filter.SecondaryGroup}");
        if (hasL3) sb.AppendLine($"# Third Group: {filter.ThirdGroup}");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        var cols = new List<(string Key, string Label, bool IsNumeric, Func<CatalogueRow, string> Value)>();
        if (hasL1) cols.Add(("_L1", "Group 1", false, r => r.Level1Value ?? ""));
        if (hasL2) cols.Add(("_L2", "Group 2", false, r => r.Level2Value ?? ""));
        if (hasL3) cols.Add(("_L3", "Group 3", false, r => r.Level3Value ?? ""));
        if (showItem && dc("ItemCode")) cols.Add(("ItemCode", "Code", false, r => r.ItemCode ?? ""));
        if (showItem && dc("MainBarcode")) cols.Add(("MainBarcode", "Barcode", false, r => r.MainBarcode ?? ""));
        if (showItem && dc("ItemName")) cols.Add(("ItemName", "Description", false, r => r.ItemDescription ?? ""));
        if (dc("Quantity")) cols.Add(("Quantity", "Qty", true, r => r.Quantity.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Value")) cols.Add(("Value", "Value", true, r => r.ValueBeforeDiscount.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Discount")) cols.Add(("Discount", "Discount", true, r => r.Discount.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("NetValue")) cols.Add(("NetValue", "Net Value", true, r => r.NetValue.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("VatAmount")) cols.Add(("VatAmount", "VAT", true, r => r.VatAmount.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("GrossAmount")) cols.Add(("GrossAmount", "Gross Amt", true, r => r.GrossAmount.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Profit")    && viewCost) cols.Add(("Profit", "Profit", true, r => r.ProfitValue.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Markup")    && viewCost) cols.Add(("Markup", "Markup %", true, r => r.Markup.ToString("F1", CultureInfo.InvariantCulture)));
        if (dc("Margin")    && viewCost) cols.Add(("Margin", "Margin %", true, r => r.Margin.ToString("F1", CultureInfo.InvariantCulture)));
        if (dc("Cost")      && viewCost) cols.Add(("Cost", "Cost", true, r => r.Cost.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalCost") && viewCost) cols.Add(("TotalCost", "Total Cost", true, r => r.TotalCost.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalStockQty")) cols.Add(("TotalStockQty", "Stock Qty", true, r => r.TotalStockQty.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalStockValue")) cols.Add(("TotalStockValue", "Stock Value", true, r => r.TotalStockValue.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("EntityCode")) cols.Add(("EntityCode", "Entity Code", false, r => r.EntityCode ?? ""));
        if (dc("EntityName")) cols.Add(("EntityName", "Entity Name", false, r => r.EntityName ?? ""));
        if (dc("EntityTel1")) cols.Add(("EntityTel1", "Phone", false, r => r.EntityTel1 ?? ""));
        if (dc("EntityTel2")) cols.Add(("EntityTel2", "Phone 2", false, r => r.EntityTel2 ?? ""));
        if (dc("EntityMobile")) cols.Add(("EntityMobile", "Mobile", false, r => r.EntityMobile ?? ""));
        if (dc("EntityFax")) cols.Add(("EntityFax", "Fax", false, r => r.EntityFax ?? ""));
        if (dc("EntityEmail")) cols.Add(("EntityEmail", "Email", false, r => r.EntityEmail ?? ""));
        if (dc("EntityContactName")) cols.Add(("EntityContactName", "Contact", false, r => r.EntityContactName ?? ""));
        if (dc("EntityVatRegNo")) cols.Add(("EntityVatRegNo", "VAT Reg No", false, r => r.EntityVatRegNo ?? ""));
        if (dc("EntityDOB")) cols.Add(("EntityDOB", "Date of Birth", false, r => r.EntityDOB?.ToString("yyyy-MM-dd") ?? ""));
        if (dc("InvoiceNumber")) cols.Add(("InvoiceNumber", "Invoice No", false, r => r.InvoiceNumber ?? ""));
        if (dc("InvoiceType")) cols.Add(("InvoiceType", "Inv. Type", false, r => r.InvoiceType ?? ""));
        if (dc("StoreCode")) cols.Add(("StoreCode", "Store Code", false, r => r.StoreCode ?? ""));
        if (dc("StoreName")) cols.Add(("StoreName", "Store", false, r => r.StoreName ?? ""));
        if (dc("StationCode")) cols.Add(("StationCode", "Station", false, r => r.StationCode ?? ""));
        if (dc("DateTrans")) cols.Add(("DateTrans", "Date", false, r => r.DateTrans?.ToString("yyyy-MM-dd") ?? ""));
        if (dc("UserCode")) cols.Add(("UserCode", "User", false, r => r.UserCode ?? ""));
        if (dc("AgentName")) cols.Add(("AgentName", "Agent", false, r => r.AgentName ?? ""));
        if (dc("ZReportNumber")) cols.Add(("ZReportNumber", "Z Report", false, r => r.ZReportNumber ?? ""));
        if (dc("PaymentType")) cols.Add(("PaymentType", "Payment Type", false, r => r.PaymentType ?? ""));
        if (dc("ItemCategory")) cols.Add(("ItemCategory", "Category", false, r => r.ItemCategoryDescr ?? ""));
        if (dc("ItemDepartment")) cols.Add(("ItemDepartment", "Department", false, r => r.ItemDepartmentDescr ?? ""));
        if (dc("Brand")) cols.Add(("Brand", "Brand", false, r => r.BrandName ?? ""));
        if (dc("Season")) cols.Add(("Season", "Season", false, r => r.SeasonName ?? ""));
        if (dc("Model")) cols.Add(("Model", "Model", false, r => r.ModelCode ?? ""));
        if (dc("Colour")) cols.Add(("Colour", "Colour", false, r => r.Colour ?? ""));
        if (dc("Size")) cols.Add(("Size", "Size", false, r => r.Size ?? ""));
        if (dc("Franchise")) cols.Add(("Franchise", "Franchise", false, r => r.FranchiseName ?? ""));
        if (dc("ItemSupplier") && viewSupplier) cols.Add(("ItemSupplier", "Supplier", false, r => r.ItemSupplierName ?? ""));
        if (dc("Price1Excl")) cols.Add(("Price1Excl", "Price 1 Ex", true, r => r.Price1Excl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Price1Incl")) cols.Add(("Price1Incl", "Price 1 In", true, r => r.Price1Incl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Price2Excl")) cols.Add(("Price2Excl", "Price 2 Ex", true, r => r.Price2Excl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Price2Incl")) cols.Add(("Price2Incl", "Price 2 In", true, r => r.Price2Incl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Price3Excl")) cols.Add(("Price3Excl", "Price 3 Ex", true, r => r.Price3Excl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Price3Incl")) cols.Add(("Price3Incl", "Price 3 In", true, r => r.Price3Incl.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("ItemAttr1")) cols.Add(("ItemAttr1", "Attr 1", false, r => r.ItemAttr1Descr ?? ""));
        if (dc("ItemAttr2")) cols.Add(("ItemAttr2", "Attr 2", false, r => r.ItemAttr2Descr ?? ""));
        if (dc("ItemAttr3")) cols.Add(("ItemAttr3", "Attr 3", false, r => r.ItemAttr3Descr ?? ""));
        if (dc("ItemAttr4")) cols.Add(("ItemAttr4", "Attr 4", false, r => r.ItemAttr4Descr ?? ""));
        if (dc("ItemAttr5")) cols.Add(("ItemAttr5", "Attr 5", false, r => r.ItemAttr5Descr ?? ""));
        if (dc("ItemAttr6")) cols.Add(("ItemAttr6", "Attr 6", false, r => r.ItemAttr6Descr ?? ""));

        sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Label))));

        string FmtCat(string key, decimal v) =>
            (key == "Markup" || key == "Margin")
                ? v.ToString("F1", CultureInfo.InvariantCulture)
                : v.ToString("F2", CultureInfo.InvariantCulture);

        // Subtotal row: group identity is in the _L1/_L2/_L3 columns (matches PurchasesSales).
        // Math delegated to CatalogueSubtotal so the numbers match the grid/preview exactly.
        void WriteSubtotal(IEnumerable<CatalogueRow> grp, string label, int level)
        {
            var st = CatalogueSubtotal.From(grp);
            int labelCol = cols.FindIndex(c => c.Key == "_L" + level);
            var cells = new List<string>(cols.Count);
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i].IsNumeric)
                {
                    var v = st.ValueForKey(cols[i].Key);
                    cells.Add(v.HasValue ? FmtCat(cols[i].Key, v.Value) : "");
                }
                else cells.Add(i == labelCol ? label : "");
            }
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
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
                                foreach (var row in l3Rows)
                                    sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Value(row)))));
                                WriteSubtotal(l3Rows, l3.Key + " subtotal", 3);
                            }
                        }
                        else
                        {
                            foreach (var row in l2Rows)
                                sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Value(row)))));
                        }
                        WriteSubtotal(l2Rows, l2.Key + " subtotal", 2);
                    }
                }
                else
                {
                    foreach (var row in l1Rows)
                        sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Value(row)))));
                }
                WriteSubtotal(l1Rows, l1.Key + " total", 1);
            }
        }
        else
        {
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Value(row)))));
        }

        if (totals != null && cols.Count > 0)
        {
            var totalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity"] = totals.TotalQuantity.ToString("F2", CultureInfo.InvariantCulture),
                ["Value"] = totals.TotalValueBeforeDiscount.ToString("F2", CultureInfo.InvariantCulture),
                ["Discount"] = totals.TotalDiscount.ToString("F2", CultureInfo.InvariantCulture),
                ["NetValue"] = totals.TotalNetValue.ToString("F2", CultureInfo.InvariantCulture),
                ["VatAmount"] = totals.TotalVatAmount.ToString("F2", CultureInfo.InvariantCulture),
                ["GrossAmount"] = totals.TotalGrossAmount.ToString("F2", CultureInfo.InvariantCulture),
                ["Profit"] = totals.TotalProfitValue.ToString("F2", CultureInfo.InvariantCulture),
                ["Markup"] = totals.TotalMarkup.ToString("F1", CultureInfo.InvariantCulture),
                ["Margin"] = totals.TotalMargin.ToString("F1", CultureInfo.InvariantCulture),
                ["TotalCost"] = totals.TotalTotalCost.ToString("F2", CultureInfo.InvariantCulture),
                ["TotalStockQty"] = totals.TotalStockQty.ToString("F2", CultureInfo.InvariantCulture),
                ["TotalStockValue"] = totals.TotalStockValue.ToString("F2", CultureInfo.InvariantCulture)
            };

            var totalCells = new List<string>();
            bool labelPlaced = false;
            for (int i = 0; i < cols.Count; i++)
            {
                if (totalMap.TryGetValue(cols[i].Key, out var v)) totalCells.Add(v);
                else if (!labelPlaced && !cols[i].IsNumeric) { totalCells.Add(i == 0 ? "GRAND TOTAL" : ""); if (i == 0) labelPlaced = true; }
                else totalCells.Add("");
            }
            if (!labelPlaced && totalCells.Count > 0) totalCells[0] = "GRAND TOTAL";
            sb.AppendLine(string.Join(",", totalCells.Select(Escape)));
        }

        return new UTF8Encoding(true).GetBytes(sb.ToString());
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

    public byte[] GenerateParetoCsv(ParetoResult result, ParetoFilter filter, bool viewCost = true)
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

        var header = new List<string> { "Rank", "Code", "Name", "Quantity", "Subtotal" };
        if (viewCost) header.Add("Profit");
        header.AddRange(new[] { "%", "Cumul. %", "Class", "Display" });
        sb.AppendLine(string.Join(",", header.Select(Escape)));

        foreach (var row in result.Rows)
        {
            var cells = new List<string>
            {
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.Code,
                row.Name,
                row.Quantity.ToString("F4", CultureInfo.InvariantCulture),
                row.Subtotal.ToString("F2", CultureInfo.InvariantCulture)
            };
            if (viewCost) cells.Add(row.Profit.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.Percentage.ToString("F2", CultureInfo.InvariantCulture));
            cells.Add(row.CumulativePercentage.ToString("F1", CultureInfo.InvariantCulture));
            cells.Add(row.Classification);
            cells.Add(row.IsDisplay ? "Yes" : "");
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        var totalCells = new List<string> { "", "", "TOTAL",
            result.TotalQuantity.ToString("F4", CultureInfo.InvariantCulture),
            result.TotalSubtotal.ToString("F2", CultureInfo.InvariantCulture) };
        if (viewCost) totalCells.Add(result.TotalProfit.ToString("F2", CultureInfo.InvariantCulture));
        totalCells.AddRange(new[] { "100.00", "", "", "" });
        sb.AppendLine(string.Join(",", totalCells.Select(Escape)));

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    public byte[] GenerateCancelLogCsv(
        List<CancelLogDetailedRow>? detailedRows,
        List<CancelLogSummaryRow>? summaryRows,
        CancelLogFilter filter)
    {
        bool isDetailed = filter.ReportType == CancelLogReportType.Detailed;
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";

        var sb = new StringBuilder();

        sb.AppendLine("# Cancellation Logging Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Report Type: {filter.ReportType}");
        sb.AppendLine($"# Action Type: {filter.ActionType}");
        if (hasL1) sb.AppendLine($"# Primary Group: {filter.PrimaryGroup}");
        if (hasL2) sb.AppendLine($"# Secondary Group: {filter.SecondaryGroup}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        if (isDetailed)
        {
            var headers = new List<string> { "Store/Station" };
            if (hasL1) headers.Add("Group 1");
            if (hasL2) headers.Add("Group 2");
            headers.AddRange(new[]
            {
                "Action", "Session Date", "Trans Kind", "Customer",
                "Item Code", "Item Descr", "User", "Invoice/Credit",
                "Z Report", "Total Lines", "Invoice Total",
                "Quantity", "Amount", "Table No", "Table Name", "Compartment"
            });
            sb.AppendLine(string.Join(",", headers.Select(Escape)));

            void WriteDetRow(CancelLogDetailedRow row)
            {
                var cells = new List<string> { row.StoreAndStation };
                if (hasL1) cells.Add(row.Level1Descr);
                if (hasL2) cells.Add(row.Level2Descr);
                cells.Add(row.ActionType);
                cells.Add(row.SessionDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "");
                cells.Add(row.TransKind == "I" ? "Sale" : "Return");
                cells.Add(row.CustomerFullName);
                cells.Add(row.ItemCode);
                cells.Add(row.ItemDescr);
                cells.Add(row.UserCode);
                cells.Add(!string.IsNullOrEmpty(row.InvoiceId) ? row.InvoiceId : row.CreditId);
                cells.Add(row.ZReport);
                cells.Add(row.TotalInvoiceLines.ToString(CultureInfo.InvariantCulture));
                cells.Add(row.InvoiceTotal.ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.Quantity.ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.Amount.ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.TableNo);
                cells.Add(row.TableName);
                cells.Add(row.CompartmentName);
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
            }

            void WriteDetSub(List<CancelLogDetailedRow> g, string label)
            {
                var cells = new List<string> { label };
                if (hasL1) cells.Add("");
                if (hasL2) cells.Add("");
                for (int i = 0; i < 10; i++) cells.Add(""); // Action..Total Lines
                cells.Add(g.Sum(x => x.InvoiceTotal).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.Quantity).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.Amount).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(""); cells.Add(""); cells.Add(""); // Table No, Table Name, Compartment
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
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
                            foreach (var row in l2Rows) WriteDetRow(row);
                            WriteDetSub(l2Rows, l2.Key + " subtotal");
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteDetRow(row);
                    }
                    WriteDetSub(l1Rows, l1.Key + " total");
                }
            }
            else
            {
                foreach (var row in det) WriteDetRow(row);
            }
        }
        else
        {
            var headers = new List<string> { "Store/Station" };
            if (hasL1) headers.Add("Group 1");
            if (hasL2) headers.Add("Group 2");
            headers.AddRange(new[]
            {
                "Deleted", "Cancelled", "Complimentary",
                "Invoice Total", "Quantity", "Amount"
            });
            sb.AppendLine(string.Join(",", headers.Select(Escape)));

            void WriteSumRow(CancelLogSummaryRow row)
            {
                var cells = new List<string> { row.StoreAndStation };
                if (hasL1) cells.Add(row.Level1Descr);
                if (hasL2) cells.Add(row.Level2Descr);
                cells.Add(row.DeletedAction.ToString(CultureInfo.InvariantCulture));
                cells.Add(row.CancelledAction.ToString(CultureInfo.InvariantCulture));
                cells.Add(row.ComplimentaryAction.ToString(CultureInfo.InvariantCulture));
                cells.Add(row.InvoiceTotal.ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.Quantity.ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(row.Amount.ToString("F2", CultureInfo.InvariantCulture));
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
            }

            void WriteSumSub(List<CancelLogSummaryRow> g, string label)
            {
                var cells = new List<string> { label };
                if (hasL1) cells.Add("");
                if (hasL2) cells.Add("");
                cells.Add(g.Sum(x => x.DeletedAction).ToString(CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.CancelledAction).ToString(CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.ComplimentaryAction).ToString(CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.InvoiceTotal).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.Quantity).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add(g.Sum(x => x.Amount).ToString("F2", CultureInfo.InvariantCulture));
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
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
                            foreach (var row in l2Rows) WriteSumRow(row);
                            WriteSumSub(l2Rows, l2.Key + " subtotal");
                        }
                    }
                    else
                    {
                        foreach (var row in l1Rows) WriteSumRow(row);
                    }
                    WriteSumSub(l1Rows, l1.Key + " total");
                }
            }
            else
            {
                foreach (var row in sum) WriteSumRow(row);
            }
        }

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    public byte[] GenerateProspectClientsCsv(
        List<ProspectClientsRow> rows, ProspectClientsFilter filter)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";

        var sb = new StringBuilder();

        var headers = new List<string>();
        if (hasL1) headers.Add("Group 1");
        if (hasL2) headers.Add("Group 2");
        headers.AddRange(new[]
        {
            "Lead No", "Company/Name", "Contact Person", "Status", "Priority",
            "Registration Date", "Last Modified", "Next Communication",
            "Phone", "Mobile", "Email", "Town",
            "Followed By", "Recommended By", "Linked Customer",
            "Category 1", "Category 2", "Notes",
            "Offers", "Offer Value", "Emails Sent", "SMS Sent"
        });
        if (filter.IncludeHistory) headers.Add("Source");
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        void WriteRow(ProspectClientsRow row)
        {
            var vals = new List<string>();
            if (hasL1) vals.Add(row.Level1Descr);
            if (hasL2) vals.Add(row.Level2Descr);
            vals.Add(row.LeadNo);
            vals.Add(row.CompanyName);
            vals.Add(row.ContactPerson);
            vals.Add(row.StatusName);
            vals.Add(row.PriorityName);
            vals.Add(row.RegistrationDate?.ToString("yyyy-MM-dd") ?? "");
            vals.Add(row.LastModification?.ToString("yyyy-MM-dd") ?? "");
            vals.Add(row.NextCommunicationDate?.ToString("yyyy-MM-dd") ?? "");
            vals.Add(row.Tel1);
            vals.Add(row.Mobile);
            vals.Add(row.Email);
            vals.Add(row.Town);
            vals.Add(row.FollowedBy);
            vals.Add(row.RecommendedBy);
            vals.Add(row.LinkedCustomer);
            vals.Add(row.Category1);
            vals.Add(row.Category2);
            vals.Add(row.Notes);
            vals.Add(row.OfferCount.ToString());
            vals.Add(row.TotalOfferValue.ToString("F2"));
            vals.Add(row.EmailsSent.ToString());
            vals.Add(row.SmsSent.ToString());
            if (filter.IncludeHistory) vals.Add(row.Source);
            sb.AppendLine(string.Join(",", vals.Select(Escape)));
        }

        void WriteSub(List<ProspectClientsRow> g, string label)
        {
            int lead = 18 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0);
            var vals = new List<string> { label };
            for (int i = 1; i < lead; i++) vals.Add("");
            vals.Add(g.Sum(x => x.OfferCount).ToString());
            vals.Add(g.Sum(x => x.TotalOfferValue).ToString("F2"));
            vals.Add(g.Sum(x => x.EmailsSent).ToString());
            vals.Add(g.Sum(x => x.SmsSent).ToString());
            if (filter.IncludeHistory) vals.Add("");
            sb.AppendLine(string.Join(",", vals.Select(Escape)));
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

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public string GenerateProspectClientsCsvString(
        List<ProspectClientsRow> rows, ProspectClientsFilter filter)
    {
        var bytes = GenerateProspectClientsCsv(rows, filter);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] GenerateOffersReportCsv(
        List<OffersReportRow> rows, OffersReportFilter filter, bool viewCost = true)
    {
        bool hasL1 = filter.PrimaryGroup != "NONE";
        bool hasL2 = filter.SecondaryGroup != "NONE";
        bool hasL3 = filter.ThirdGroup != "NONE";

        var sb = new StringBuilder();

        var headers = new List<string>();
        if (hasL1) headers.Add("Group 1");
        if (hasL2) headers.Add("Group 2");
        if (hasL3) headers.Add("Group 3");
        headers.AddRange(new[]
        {
            "Offer No", "Date", "Valid Until", "Status",
            "Customer", "Store", "Agent",
            "Items", "Qty", "Subtotal", "Discount", "Disc %",
            "VAT", "Grand Total"
        });
        if (viewCost) headers.Add("Cost");
        headers.AddRange(new[]
        {
            "Order %",
            "Lead", "Printed", "Emailed", "Comments", "Internal Notes", "Source"
        });
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        void WriteRow(OffersReportRow row)
        {
            var vals = new List<string>();
            if (hasL1) vals.Add(row.Level1Descr);
            if (hasL2) vals.Add(row.Level2Descr);
            if (hasL3) vals.Add(row.Level3Descr);
            vals.Add(row.OfferNo);
            vals.Add(row.DateTrans?.ToString("yyyy-MM-dd") ?? "");
            vals.Add(row.ValidUntil?.ToString("yyyy-MM-dd") ?? "");
            vals.Add(row.StatusName);
            vals.Add(row.CustomerName);
            vals.Add(row.StoreName);
            vals.Add(row.AgentName);
            vals.Add(row.ItemCount.ToString());
            vals.Add(row.TotalQuantity.ToString("F2"));
            vals.Add(row.InvoiceTotal.ToString("F2"));
            vals.Add(row.InvoiceTotalDiscount.ToString("F2"));
            vals.Add(row.InvoiceDiscountPerc.ToString("F2"));
            vals.Add(row.InvoiceVat.ToString("F2"));
            vals.Add(row.InvoiceGrandTotal.ToString("F2"));
            if (viewCost) vals.Add(row.TotalItemCost.ToString("F2"));
            vals.Add(row.OrderPercentage.ToString("F2"));
            vals.Add(row.LinkedLead);
            vals.Add(row.Printed ? "Yes" : "");
            vals.Add(row.SentByEmail ? "Yes" : "");
            vals.Add(row.Comments);
            vals.Add(row.InternalNotes);
            vals.Add(row.Source);
            sb.AppendLine(string.Join(",", vals.Select(Escape)));
        }

        // Subtotal: additive numeric columns are summed; percentages (Disc %, Order %) and text
        // columns are left blank (matches the on-screen grid).
        void WriteSub(List<OffersReportRow> g, string label)
        {
            int lead = 7 + (hasL1 ? 1 : 0) + (hasL2 ? 1 : 0) + (hasL3 ? 1 : 0); // groups + OfferNo..Agent
            var vals = new List<string> { label };
            for (int i = 1; i < lead; i++) vals.Add("");
            vals.Add(g.Sum(x => x.ItemCount).ToString());
            vals.Add(g.Sum(x => x.TotalQuantity).ToString("F2"));
            vals.Add(g.Sum(x => x.InvoiceTotal).ToString("F2"));
            vals.Add(g.Sum(x => x.InvoiceTotalDiscount).ToString("F2"));
            vals.Add(""); // Disc %
            vals.Add(g.Sum(x => x.InvoiceVat).ToString("F2"));
            vals.Add(g.Sum(x => x.InvoiceGrandTotal).ToString("F2"));
            if (viewCost) vals.Add(g.Sum(x => x.TotalItemCost).ToString("F2"));
            vals.Add(""); // Order %
            for (int i = 0; i < 6; i++) vals.Add(""); // Lead, Printed, Emailed, Comments, Internal Notes, Source
            sb.AppendLine(string.Join(",", vals.Select(Escape)));
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

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public string GenerateOffersReportCsvString(
        List<OffersReportRow> rows, OffersReportFilter filter, bool viewCost = true)
    {
        var bytes = GenerateOffersReportCsv(rows, filter, viewCost);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] GenerateTrialBalanceCsv(List<TrialBalanceRow> rows, TrialBalanceFilter filter)
    {
        rows ??= new();
        bool isSummary = filter.ReportMode == TrialBalanceReportMode.Summary;

        var sb = new StringBuilder();
        sb.AppendLine("# Trial Balance");
        sb.AppendLine($"# As At: {filter.AsAt:dd/MM/yyyy}");
        sb.AppendLine($"# Report Mode: {filter.ReportMode}");
        sb.AppendLine($"# Include Zero Movements: {(filter.IncludeZeroMovements ? "Yes" : "No")}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        static string Num(decimal v) => v == 0 ? "" : v.ToString("F2", CultureInfo.InvariantCulture);

        if (isSummary)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                "Header Code", "Header", "Opening DR", "Opening CR",
                "Debit", "Credit", "Closing DR", "Closing CR"
            }.Select(Escape)));

            foreach (var g in rows.GroupBy(r => new { r.HeaderKey, r.HeaderCode, r.HeaderName })
                                   .OrderBy(g => g.First().HeaderCodeSort, StringComparer.Ordinal))
            {
                var list = g.ToList();
                var cells = new List<string>
                {
                    g.Key.HeaderCode,
                    g.Key.HeaderName,
                    Num(list.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance)),
                    Num(list.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance)),
                    Num(list.Sum(r => r.DebitMovement)),
                    Num(list.Sum(r => r.CreditMovement)),
                    Num(list.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance)),
                    Num(list.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance))
                };
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
            }
        }
        else
        {
            sb.AppendLine(string.Join(",", new[]
            {
                "Header Code", "Header", "Account Code", "Account",
                "Opening DR", "Opening CR", "Debit", "Credit", "Closing DR", "Closing CR"
            }.Select(Escape)));

            foreach (var row in rows)
            {
                var cells = new List<string>
                {
                    row.HeaderCode,
                    row.HeaderName,
                    row.AccountCode,
                    row.AccountName,
                    Num(row.OpeningBalanceType == "DR" ? row.OpeningBalance : 0),
                    Num(row.OpeningBalanceType == "CR" ? row.OpeningBalance : 0),
                    Num(row.DebitMovement),
                    Num(row.CreditMovement),
                    Num(row.ClosingBalanceType == "DR" ? row.ClosingBalance : 0),
                    Num(row.ClosingBalanceType == "CR" ? row.ClosingBalance : 0)
                };
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
            }
        }

        // Grand totals (suppressed rows still count, mirroring legacy report totals).
        sb.AppendLine();
        var totalCells = new List<string> { "TOTAL", "" };
        if (!isSummary) { totalCells.Add(""); totalCells.Add(""); }
        totalCells.Add(Num(rows.Where(r => r.OpeningBalanceType == "DR").Sum(r => r.OpeningBalance)));
        totalCells.Add(Num(rows.Where(r => r.OpeningBalanceType == "CR").Sum(r => r.OpeningBalance)));
        totalCells.Add(Num(rows.Sum(r => r.DebitMovement)));
        totalCells.Add(Num(rows.Sum(r => r.CreditMovement)));
        totalCells.Add(Num(rows.Where(r => r.ClosingBalanceType == "DR").Sum(r => r.ClosingBalance)));
        totalCells.Add(Num(rows.Where(r => r.ClosingBalanceType == "CR").Sum(r => r.ClosingBalance)));
        sb.AppendLine(string.Join(",", totalCells.Select(Escape)));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] GenerateProfitLossCsv(List<ProfitLossRow> rows, ProfitLossFilter filter)
    {
        rows ??= new();
        bool compare = filter.CompareToLastYear;
        var visible = rows.Where(r => !r.Suppressed).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Profit & Loss");
        sb.AppendLine($"# Period: {filter.DateFrom:dd/MM/yyyy} - {filter.DateTo:dd/MM/yyyy}");
        sb.AppendLine($"# Header Level: {(filter.HeaderLevel ? "Yes" : "No")}");
        if (compare)
            sb.AppendLine($"# Comparison Period: {filter.PriorDateFrom:dd/MM/yyyy} - {filter.PriorDateTo:dd/MM/yyyy}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        static string Num(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);
        static string Pct(decimal? v) => v.HasValue ? v.Value.ToString("F2", CultureInfo.InvariantCulture) : "";

        var header = new List<string> { "Group", "Account Code", "Account", "Amount" };
        if (compare) { header.Add("Prior Year"); header.Add("Variance"); header.Add("Variance %"); }
        sb.AppendLine(string.Join(",", header.Select(Escape)));

        foreach (var grp in visible.GroupBy(r => r.Group).OrderBy(g => (int)g.Key))
        {
            var list = grp.ToList();
            foreach (var row in list)
            {
                var cells = new List<string>
                {
                    row.GroupName, row.AccountCode, row.AccountName, Num(row.Balance)
                };
                if (compare) { cells.Add(Num(row.PriorBalance)); cells.Add(Num(row.Variance)); cells.Add(Pct(row.VariancePercent)); }
                sb.AppendLine(string.Join(",", cells.Select(Escape)));
            }

            var sub = new List<string> { list[0].GroupName + " subtotal", "", "", Num(list.Sum(r => r.Balance)) };
            if (compare) { sub.Add(Num(list.Sum(r => r.PriorBalance))); sub.Add(Num(list.Sum(r => r.Variance))); sub.Add(""); }
            sb.AppendLine(string.Join(",", sub.Select(Escape)));
        }

        decimal Tot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.Balance);
        decimal PriorTot(ProfitLossGroup g) => visible.Where(r => r.Group == g).Sum(r => r.PriorBalance);
        decimal gross = Tot(ProfitLossGroup.Sales) - Tot(ProfitLossGroup.CostOfSales);
        decimal net = gross + Tot(ProfitLossGroup.Income) - Tot(ProfitLossGroup.Expenses);
        decimal pGross = PriorTot(ProfitLossGroup.Sales) - PriorTot(ProfitLossGroup.CostOfSales);
        decimal pNet = pGross + PriorTot(ProfitLossGroup.Income) - PriorTot(ProfitLossGroup.Expenses);

        sb.AppendLine();
        void Summary(string label, decimal cur, decimal prior)
        {
            var cells = new List<string> { label, "", "", Num(cur) };
            if (compare) { cells.Add(Num(prior)); cells.Add(Num(cur - prior)); cells.Add(""); }
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        Summary("GROSS PROFIT", gross, pGross);
        Summary("NET PROFIT", net, pNet);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] GenerateBelowMinStockCsv(List<BelowMinStockRow> rows, BelowMinStockFilter filter, bool viewCost = true)
    {
        rows ??= new();
        var sb = new StringBuilder();
        sb.AppendLine("# Below Minimum Stock Report");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"# Total Items: {rows.Count}");
        sb.AppendLine();

        var header = new List<string> { "Item Code", "Item Name", "Store", "Store Name", "Category", "Department", "Brand", "Current Stock", "Minimum Stock", "Difference" };
        if (viewCost) { header.Add("Cost"); header.Add("Stock Value"); }
        header.Add("Shelf");
        sb.AppendLine(string.Join(",", header.Select(Escape)));

        foreach (var r in rows)
        {
            var cells = new List<string>
            {
                r.ItemCode, r.ItemName, r.StoreCode, r.StoreName,
                r.CategoryName ?? "", r.DepartmentName ?? "", r.BrandName ?? "",
                r.CurrentStock.ToString("F0", CultureInfo.InvariantCulture),
                r.MinimumStock.ToString("F0", CultureInfo.InvariantCulture),
                r.Difference.ToString("F0", CultureInfo.InvariantCulture)
            };
            if (viewCost)
            {
                cells.Add((r.Cost ?? 0).ToString("F2", CultureInfo.InvariantCulture));
                cells.Add((r.StockValue ?? 0).ToString("F2", CultureInfo.InvariantCulture));
            }
            cells.Add(r.Shelf ?? "");
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
