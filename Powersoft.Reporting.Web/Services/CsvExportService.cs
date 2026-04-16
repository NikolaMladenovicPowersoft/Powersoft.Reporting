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

        sb.AppendLine($"# Average Basket Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Breakdown: {filter.Breakdown}");
        sb.AppendLine($"# Group By: {filter.GroupBy}");
        if (filter.SecondaryGroupBy != Core.Enums.GroupByType.None)
            sb.AppendLine($"# Secondary Group: {filter.SecondaryGroupBy}");
        sb.AppendLine($"# Include VAT: {(includeVat ? "Yes" : "No")}");
        if (compareLY) sb.AppendLine("# Compare Last Year: Yes");
        if (filter.StoreCodes != null && filter.StoreCodes.Any())
            sb.AppendLine($"# Stores: {string.Join(", ", filter.StoreCodes)}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

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

        sb.AppendLine($"# Purchases vs Sales Report");
        sb.AppendLine($"# Period: {filter.DateFrom:yyyy-MM-dd} to {filter.DateTo:yyyy-MM-dd}");
        sb.AppendLine($"# Mode: {filter.ReportMode}");
        if (hasL1) sb.AppendLine($"# Primary Group: {filter.PrimaryGroup}");
        if (hasL2) sb.AppendLine($"# Secondary Group: {filter.SecondaryGroup}");
        if (hasL3) sb.AppendLine($"# Third Group: {filter.ThirdGroup}");
        sb.AppendLine($"# Include VAT: {(filter.IncludeVat ? "Yes" : "No")}");
        if (filter.ShowProfit) sb.AppendLine("# Show Profit: Yes");
        if (filter.ShowStock) sb.AppendLine("# Show Stock: Yes");
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
        sb.AppendLine(string.Join(",", cells.Select(Escape)));
    }

    public byte[] GenerateCatalogueCsv(
        List<CatalogueRow> rows,
        CatalogueTotals? totals,
        CatalogueFilter filter)
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
        if (dc("Profit")) cols.Add(("Profit", "Profit", true, r => r.ProfitValue.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("Markup")) cols.Add(("Markup", "Markup %", true, r => r.Markup.ToString("F1", CultureInfo.InvariantCulture)));
        if (dc("Margin")) cols.Add(("Margin", "Margin %", true, r => r.Margin.ToString("F1", CultureInfo.InvariantCulture)));
        if (dc("Cost")) cols.Add(("Cost", "Cost", true, r => r.Cost.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalCost")) cols.Add(("TotalCost", "Total Cost", true, r => r.TotalCost.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalStockQty")) cols.Add(("TotalStockQty", "Stock Qty", true, r => r.TotalStockQty.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("TotalStockValue")) cols.Add(("TotalStockValue", "Stock Value", true, r => r.TotalStockValue.ToString("F2", CultureInfo.InvariantCulture)));
        if (dc("EntityCode")) cols.Add(("EntityCode", "Entity Code", false, r => r.EntityCode ?? ""));
        if (dc("EntityName")) cols.Add(("EntityName", "Entity Name", false, r => r.EntityName ?? ""));
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
        if (dc("ItemSupplier")) cols.Add(("ItemSupplier", "Supplier", false, r => r.ItemSupplierName ?? ""));
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

        foreach (var row in rows)
            sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Value(row)))));

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

    public byte[] GenerateParetoCsv(ParetoResult result, ParetoFilter filter)
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

        var metricLabel = filter.Metric == Core.Models.ParetoMetric.Quantity ? "Quantity" : "Value";
        sb.AppendLine(string.Join(",", new[] { "Rank", "Code", "Name", metricLabel, "%", "Cumul. %", "Class" }.Select(Escape)));

        foreach (var row in result.Rows)
        {
            var cells = new List<string>
            {
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.Code,
                row.Name,
                row.Value.ToString("F2", CultureInfo.InvariantCulture),
                row.Percentage.ToString("F2", CultureInfo.InvariantCulture),
                row.CumulativePercentage.ToString("F1", CultureInfo.InvariantCulture),
                row.Classification
            };
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        var totalCells = new List<string> { "", "", "TOTAL",
            result.GrandTotal.ToString("F2", CultureInfo.InvariantCulture), "100.00", "", "" };
        sb.AppendLine(string.Join(",", totalCells.Select(Escape)));

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
