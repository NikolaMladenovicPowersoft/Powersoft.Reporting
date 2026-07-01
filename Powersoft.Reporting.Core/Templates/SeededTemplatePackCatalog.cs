using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Templates;

/// <summary>
/// POC catalog: template packs defined in code. Every ParametersJson here is STRUCTURAL only
/// (report mode, grouping enum, relative date range) — no tenant-specific IDs — so a pack applies
/// cleanly to any company. Keys/values mirror exactly what the report modals emit and what
/// <see cref="Core.Helpers.ScheduleParametersParser"/> reads back (camelCase keys, string enum names).
///
/// This is the POC data source only; the authoring mechanism (central catalog vs schedule-builder
/// form) is a later decision. Adding/removing a report here requires a redeploy — acceptable for POC.
/// </summary>
public class SeededTemplatePackCatalog : ITemplatePackCatalog
{
    // Relative "last calendar month" range — portable across companies and across time.
    private const string LastMonthRange = "\"reportDateRange\":{\"type\":\"LastMonth\",\"value\":0}";

    // Purchases vs Sales, Monthly mode, grouped by Category (safe aggregate default — never per-item).
    private static readonly string PsMonthlyByCategory =
        "{\"reportMode\":\"Monthly\",\"primaryGroup\":\"Category\",\"showProfit\":true,\"includeVat\":false," + LastMonthRange + "}";

    // Average Basket, Monthly breakdown, grouped by Category.
    private static readonly string AvgBasketMonthlyByCategory =
        "{\"breakdown\":\"Monthly\",\"groupBy\":\"Category\",\"includeVat\":false," + LastMonthRange + "}";

    // Profit & Loss for last month (defaults are sensible; only the relative range is pinned).
    private static readonly string ProfitLossLastMonth =
        "{" + LastMonthRange + "}";

    private readonly List<ReportTemplatePack> _packs;

    public SeededTemplatePackCatalog()
    {
        _packs = new List<ReportTemplatePack>
        {
            new ReportTemplatePack
            {
                PackCode = "FASHION",
                PackName = "Fashion Retail",
                IndustryTag = "Fashion",
                Description = "Monthly purchases vs sales by category, average basket, and a monthly P&L.",
                SortOrder = 10,
                Items = new List<ReportTemplateItem>
                {
                    new()
                    {
                        ReportType = ReportTypeConstants.PurchasesSales,
                        TemplateName = "Purchases vs Sales by Category (Monthly)",
                        ParametersJson = PsMonthlyByCategory,
                        // AI analysis OFF by default — provisioning packs across many companies with AI on
                        // would spend tokens on every run per company. Admins opt in per schedule.
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    },
                    new()
                    {
                        ReportType = ReportTypeConstants.AverageBasket,
                        TemplateName = "Average Basket by Category (Monthly)",
                        ParametersJson = AvgBasketMonthlyByCategory,
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    },
                    new()
                    {
                        ReportType = ReportTypeConstants.ProfitLoss,
                        TemplateName = "Profit & Loss (Monthly)",
                        ParametersJson = ProfitLossLastMonth,
                        // AI analysis OFF by default — provisioning packs across many companies with AI on
                        // would spend tokens on every run per company. Admins opt in per schedule.
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    }
                }
            },
            new ReportTemplatePack
            {
                PackCode = "SUPERMARKET",
                PackName = "Supermarket",
                IndustryTag = "Supermarket",
                Description = "Monthly purchases vs sales by category, average basket, and a monthly P&L.",
                SortOrder = 20,
                Items = new List<ReportTemplateItem>
                {
                    new()
                    {
                        ReportType = ReportTypeConstants.PurchasesSales,
                        TemplateName = "Purchases vs Sales by Category (Monthly)",
                        ParametersJson = PsMonthlyByCategory,
                        // AI analysis OFF by default — provisioning packs across many companies with AI on
                        // would spend tokens on every run per company. Admins opt in per schedule.
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    },
                    new()
                    {
                        ReportType = ReportTypeConstants.AverageBasket,
                        TemplateName = "Average Basket by Category (Monthly)",
                        ParametersJson = AvgBasketMonthlyByCategory,
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    },
                    new()
                    {
                        ReportType = ReportTypeConstants.ProfitLoss,
                        TemplateName = "Profit & Loss (Monthly)",
                        ParametersJson = ProfitLossLastMonth,
                        // AI analysis OFF by default — provisioning packs across many companies with AI on
                        // would spend tokens on every run per company. Admins opt in per schedule.
                        RecurrenceType = "Monthly", RecurrenceDay = 1,
                        SkipIfEmpty = true
                    }
                }
            }
        };
    }

    /// <summary>Raw seed packs — used to populate the central catalog on a fresh install.</summary>
    public IReadOnlyList<ReportTemplatePack> Packs => _packs;

    public Task<IReadOnlyList<ReportTemplatePack>> GetPacksAsync() =>
        Task.FromResult<IReadOnlyList<ReportTemplatePack>>(_packs);

    public Task<ReportTemplatePack?> GetPackAsync(string packCode) =>
        Task.FromResult(_packs.FirstOrDefault(p =>
            string.Equals(p.PackCode, packCode, StringComparison.OrdinalIgnoreCase)));
}
