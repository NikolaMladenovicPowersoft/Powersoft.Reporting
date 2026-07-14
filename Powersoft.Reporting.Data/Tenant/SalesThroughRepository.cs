using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// Sales Through (sell-through) report — Splash/George 2026-07.
///
/// Deliberately implemented as a projection over <see cref="PurchasesSalesRepository"/>:
/// the Splash workbook (ERES SS26 ST Report) is built by exporting the legacy
/// "Power Purchases &amp; Sales" report and adding sell-through / mix columns in Excel.
/// Reusing the PS engine keeps our intake / sales / stock numbers byte-identical to the
/// report Splash already reconciles against, and inherits all its filter semantics
/// (return sign convention, additional-charges toggle, size-sequence sort, items selection).
/// </summary>
public class SalesThroughRepository : ISalesThroughRepository
{
    private readonly PurchasesSalesRepository _psRepository;

    // ST sort keys -> PS engine column names (whitelist; anything else falls back to ItemCode).
    private static readonly Dictionary<string, string> SortColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ItemCode"] = "ItemCode",
        ["ItemName"] = "ItemName",
        ["IntakeQty"] = "QuantityPurchased",
        ["IntakeValue"] = "NetPurchasedValue",
        ["SalesQty"] = "QuantitySold",
        ["SalesNet"] = "NetSoldValue",
        ["SalesGross"] = "GrossSoldValue",
        ["CurrentStock"] = "TotalStockQty"
    };

    public SalesThroughRepository(string connectionString)
    {
        _psRepository = new PurchasesSalesRepository(connectionString);
    }

    public async Task<PagedResult<SalesThroughRow>> GetSalesThroughDataAsync(SalesThroughFilter filter)
    {
        var psFilter = MapFilter(filter);
        var psResult = await _psRepository.GetPurchasesSalesDataAsync(psFilter);

        var totals = MapTotals(psResult.PsTotals);
        var rows = psResult.Items.Select(r => MapRow(r, totals)).ToList();

        return new PagedResult<SalesThroughRow>
        {
            Items = rows,
            TotalCount = psResult.TotalCount,
            PageNumber = psResult.PageNumber,
            PageSize = psResult.PageSize,
            SalesThroughTotals = totals
        };
    }

    public async Task<SalesThroughTotals> GetSalesThroughTotalsAsync(SalesThroughFilter filter)
    {
        // The PS engine computes totals alongside the data query; a single-page fetch is the
        // cheapest way to obtain just the totals when the caller does not need rows.
        var psFilter = MapFilter(filter);
        psFilter.PageNumber = 1;
        psFilter.PageSize = 1;
        var psResult = await _psRepository.GetPurchasesSalesDataAsync(psFilter);
        return MapTotals(psResult.PsTotals);
    }

    public Task<bool> TestConnectionAsync() => _psRepository.TestConnectionAsync();

    private static PurchasesSalesFilter MapFilter(SalesThroughFilter filter)
    {
        var sortColumn = SortColumnMap.TryGetValue(filter.SortColumn ?? "", out var mapped)
            ? mapped : "ItemCode";

        return new PurchasesSalesFilter
        {
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            ReportMode = filter.Summary ? PsReportMode.Summary : PsReportMode.Detailed,
            PrimaryGroup = filter.PrimaryGroup,
            SecondaryGroup = filter.SecondaryGroup,
            ThirdGroup = filter.ThirdGroup,
            IncludeVat = false,
            ShowStock = true,
            IncludeAdditionalCharges = filter.IncludeAdditionalCharges,
            SortBySizeSequence = filter.SortBySizeSequence,
            StoreCodes = filter.StoreCodes,
            ItemsSelection = filter.ItemsSelection,
            SortColumn = sortColumn,
            SortDirection = string.Equals(filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC",
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    private static SalesThroughRow MapRow(PurchasesSalesRow r, SalesThroughTotals totals)
    {
        var row = new SalesThroughRow
        {
            Level1 = r.Level1,
            Level1Value = r.Level1Value,
            Level2 = r.Level2,
            Level2Value = r.Level2Value,
            Level3 = r.Level3,
            Level3Value = r.Level3Value,
            ItemCode = r.ItemCode,
            ItemName = r.ItemName,
            IntakeQty = r.QuantityPurchased,
            IntakeValue = r.NetPurchasedValue,
            SalesQty = r.QuantitySold,
            SalesNet = r.NetSoldValue,
            SalesGross = r.GrossSoldValue,
            CurrentStock = r.TotalStockQty
        };

        // Mix % — row share of the grand total (Splash pivots: sales / intake / stock mix).
        row.SalesMixPct = totals.TotalSalesQty != 0
            ? Math.Round(row.SalesQty / totals.TotalSalesQty * 100, 2) : 0;
        row.IntakeMixPct = totals.TotalIntakeQty != 0
            ? Math.Round(row.IntakeQty / totals.TotalIntakeQty * 100, 2) : 0;
        row.StockMixPct = totals.TotalCurrentStock != 0
            ? Math.Round(row.CurrentStock / totals.TotalCurrentStock * 100, 2) : 0;

        return row;
    }

    private static SalesThroughTotals MapTotals(PurchasesSalesTotals? t) => new()
    {
        TotalIntakeQty = t?.TotalQtyPurchased ?? 0,
        TotalIntakeValue = t?.TotalNetPurchased ?? 0,
        TotalSalesQty = t?.TotalQtySold ?? 0,
        TotalSalesNet = t?.TotalNetSold ?? 0,
        TotalSalesGross = t?.TotalGrossSold ?? 0,
        TotalCurrentStock = t?.TotalStockQty ?? 0
    };
}
