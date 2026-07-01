using FluentAssertions;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Templates;
using Powersoft.Reporting.Web.Services;
using Xunit;

namespace Powersoft.Reporting.Tests.Templates;

/// <summary>
/// Guards the template-pack feature: seeded packs must contain ONLY portable structural config
/// (no tenant-specific IDs), and every ParametersJson must round-trip through the same parser the
/// scheduler uses. A pack that captured a category ID would silently filter to nothing (or crash)
/// when applied to a different company.
/// </summary>
public class TemplatePackTests
{
    private readonly SeededTemplatePackCatalog _catalog = new();

    [Fact]
    public void Catalog_HasPacks_WithUniqueCodesAndItems()
    {
        var packs = _catalog.Packs;
        packs.Should().NotBeEmpty();
        packs.Select(p => p.PackCode).Should().OnlyHaveUniqueItems();
        packs.Should().OnlyContain(p => p.Items.Count > 0);
    }

    [Fact]
    public async Task GetPack_IsCaseInsensitive()
    {
        (await _catalog.GetPackAsync("fashion")).Should().NotBeNull();
        (await _catalog.GetPackAsync("FASHION")).Should().NotBeNull();
        (await _catalog.GetPackAsync("does-not-exist")).Should().BeNull();
    }

    [Fact]
    public void EveryPack_HasUniqueItemKeys()
    {
        foreach (var pack in _catalog.Packs)
            pack.Items.Select(i => i.ItemKey).Should()
                .OnlyHaveUniqueItems($"item keys must be unique within pack '{pack.PackCode}'");
    }

    [Fact]
    public void ItemKey_IsDerivedFromTemplateName_AsStableSlug()
    {
        var item = _catalog.Packs.Single(p => p.PackCode == "FASHION").Items
            .Single(i => i.ReportType == ReportTypeConstants.PurchasesSales);
        item.ItemKey.Should().Be("purchases-vs-sales-by-category-monthly");
    }

    [Fact]
    public void IsItemApplied_HonorsLegacyWholePackAndPerItemMarkers()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FASHION",                                        // legacy: whole pack applied
            "SUPERMARKET:average-basket-by-category-monthly"  // current: single item applied
        };

        // Legacy whole-pack marker => every item of that pack counts as applied.
        TemplatePackService.IsItemApplied(keys, "FASHION", "profit-loss-monthly").Should().BeTrue();
        // Per-item marker => only that specific item.
        TemplatePackService.IsItemApplied(keys, "SUPERMARKET", "average-basket-by-category-monthly").Should().BeTrue();
        TemplatePackService.IsItemApplied(keys, "SUPERMARKET", "profit-loss-monthly").Should().BeFalse();
    }

    [Fact]
    public void EverySeededItem_UsesKnownReportType()
    {
        foreach (var item in _catalog.Packs.SelectMany(p => p.Items))
            ReportTypeConstants.IsSchedulable(item.ReportType).Should()
                .BeTrue($"'{item.ReportType}' must be a schedulable report type");
    }

    [Fact]
    public void EverySeededItem_HasNoTenantSpecificSelections()
    {
        foreach (var item in _catalog.Packs.SelectMany(p => p.Items))
        {
            TemplateParametersSanitizer.HasNonPortableSelections(item.ParametersJson)
                .Should().BeFalse($"template '{item.TemplateName}' must not pin tenant-specific IDs");
        }
    }

    [Fact]
    public void PurchasesSalesTemplate_ParsesToMonthlyByCategory_LastMonth()
    {
        var item = _catalog.Packs.Single(p => p.PackCode == "FASHION").Items
            .Single(i => i.ReportType == ReportTypeConstants.PurchasesSales);

        var p = ScheduleParametersParser.Parse(item.ParametersJson);

        p.ReportMode.Should().Be(PsReportMode.Monthly);
        p.PrimaryGroup.Should().Be(PsGroupBy.Category);
        p.DateRange.Should().NotBeNull();
        p.DateRange!.Type.Should().Be(ReportDateRangeType.LastMonth);
    }

    [Fact]
    public void AverageBasketTemplate_ParsesToMonthlyByCategory_LastMonth()
    {
        var item = _catalog.Packs.Single(p => p.PackCode == "FASHION").Items
            .Single(i => i.ReportType == ReportTypeConstants.AverageBasket);

        var p = ScheduleParametersParser.Parse(item.ParametersJson);

        p.Breakdown.Should().Be(BreakdownType.Monthly);
        p.GroupBy.Should().Be(GroupByType.Category);
        p.DateRange.Should().NotBeNull();
        p.DateRange!.Type.Should().Be(ReportDateRangeType.LastMonth);
    }

    [Fact]
    public void Sanitizer_StripsSelections_KeepsStructural()
    {
        const string withSelections =
            "{\"reportMode\":\"Monthly\",\"primaryGroup\":\"Category\"," +
            "\"reportDateRange\":{\"type\":\"LastMonth\",\"value\":0}," +
            "\"storeCodes\":\"S01,S02\",\"itemIds\":\"1,2\"," +
            "\"itemsSelectionJson\":\"{\\\"categories\\\":{\\\"ids\\\":[\\\"326\\\"]}}\"}";

        var cleaned = TemplateParametersSanitizer.Strip(withSelections);

        cleaned.Should().NotContain("storeCodes");
        cleaned.Should().NotContain("itemIds");
        cleaned.Should().NotContain("itemsSelectionJson");

        // Structural config survives and still parses correctly.
        var p = ScheduleParametersParser.Parse(cleaned);
        p.ReportMode.Should().Be(PsReportMode.Monthly);
        p.PrimaryGroup.Should().Be(PsGroupBy.Category);
        p.DateRange!.Type.Should().Be(ReportDateRangeType.LastMonth);
        p.StoreCodes.Should().BeNull();
        p.ItemIds.Should().BeNull();
        p.ItemsSelectionJson.Should().BeNull();
    }

    [Fact]
    public void Sanitizer_DetectsNonPortableSelections()
    {
        const string clean = "{\"reportMode\":\"Monthly\",\"primaryGroup\":\"Category\"}";
        const string dirty = "{\"reportMode\":\"Monthly\",\"itemsSelectionJson\":\"{\\\"categories\\\":{\\\"ids\\\":[\\\"5\\\"]}}\"}";

        TemplateParametersSanitizer.HasNonPortableSelections(clean).Should().BeFalse();
        TemplateParametersSanitizer.HasNonPortableSelections(dirty).Should().BeTrue();
    }
}
