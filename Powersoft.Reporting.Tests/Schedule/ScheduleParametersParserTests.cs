using FluentAssertions;
using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Models;
using Xunit;

namespace Powersoft.Reporting.Tests.Schedule;

/// <summary>
/// Regression coverage for the scheduler parameter parser. The bug this guards against:
/// the scheduler read storeCodes/itemIds but never read "itemsSelectionJson", so scheduled
/// reports silently dropped every Items Selection dimension filter (category/brand/supplier/...).
/// </summary>
public class ScheduleParametersParserTests
{
    // Mirrors exactly what PurchasesSales.cshtml saveSchedule() serialises into parametersJson:
    // itemsSelectionJson is a *stringified* JSON object (not a nested object).
    private const string ViewParametersJson =
        "{\"reportDateRange\":{\"type\":\"LastNDays\",\"value\":30}," +
        "\"reportMode\":\"Summary\",\"includeVat\":true,\"showStock\":true,\"showOnOrder\":true," +
        "\"storeCodes\":\"S01,S02\",\"itemIds\":\"\"," +
        "\"itemsSelectionJson\":\"{\\\"categories\\\":{\\\"ids\\\":[\\\"5\\\",\\\"9\\\"],\\\"mode\\\":\\\"include\\\"}}\"," +
        "\"sortColumn\":\"ItemCode\",\"sortDirection\":\"ASC\",\"reportType\":\"PurchasesSales\"}";

    [Fact]
    public void Parse_ReadsItemsSelectionJson_StringEncoded()
    {
        var p = ScheduleParametersParser.Parse(ViewParametersJson);

        p.ItemsSelectionJson.Should().NotBeNullOrWhiteSpace(
            "the scheduler must carry the dimension filter saved by the view");

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter.Should().NotBeNull();
        filter!.Categories.Mode.Should().Be(FilterMode.Include);
        filter.Categories.Ids.Should().BeEquivalentTo(new[] { "5", "9" });
        filter.Categories.HasFilter.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReadsItemsSelectionJson_PascalCaseKey()
    {
        var json = "{\"ItemsSelectionJson\":\"{\\\"brands\\\":{\\\"ids\\\":[\\\"3\\\"],\\\"mode\\\":\\\"exclude\\\"}}\"}";

        var p = ScheduleParametersParser.Parse(json);

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter!.Brands.Mode.Should().Be(FilterMode.Exclude);
        filter.Brands.Ids.Should().ContainSingle().Which.Should().Be("3");
    }

    [Fact]
    public void Parse_ReadsItemsSelectionJson_NestedObject()
    {
        // Defensive: some code paths may embed the selection as an object rather than a string.
        var json = "{\"itemsSelectionJson\":{\"suppliers\":{\"ids\":[\"77\"],\"mode\":\"include\"}}}";

        var p = ScheduleParametersParser.Parse(json);

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter!.Suppliers.Ids.Should().ContainSingle().Which.Should().Be("77");
        filter.Suppliers.Mode.Should().Be(FilterMode.Include);
    }

    [Fact]
    public void Parse_StillReadsLegacyStoreCodesAndShowFlags()
    {
        var p = ScheduleParametersParser.Parse(ViewParametersJson);

        p.StoreCodes.Should().BeEquivalentTo(new[] { "S01", "S02" });
        p.ShowStock.Should().BeTrue();
        p.ShowOnOrder.Should().BeTrue();
        p.IncludeVat.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void Parse_InvalidOrEmpty_ReturnsDefaultsWithNullSelection(string? json)
    {
        var p = ScheduleParametersParser.Parse(json);

        p.Should().NotBeNull();
        p.ItemsSelectionJson.Should().BeNull();
    }
}
