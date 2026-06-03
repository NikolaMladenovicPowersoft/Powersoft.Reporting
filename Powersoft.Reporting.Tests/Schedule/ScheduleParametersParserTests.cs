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

    // Mirrors collectParetoParams() in Pareto.cshtml. Before the scheduler handler was added,
    // a scheduled Pareto silently ran Average Basket and dropped all of these fields.
    private const string ParetoParametersJson =
        "{\"dimension\":\"Brand\",\"metric\":\"Profit\",\"includeVat\":true," +
        "\"excludeNegativeAmounts\":false,\"classAThreshold\":\"70\",\"classBThreshold\":\"95\"," +
        "\"profitBasis\":\"AverageCost\",\"timezoneOffsetMinutes\":-120,\"storeCodes\":\"S01,S02\"," +
        "\"itemsSelectionJson\":\"{\\\"suppliers\\\":{\\\"ids\\\":[\\\"42\\\"],\\\"mode\\\":\\\"include\\\"}}\"}";

    [Fact]
    public void Parse_ReadsParetoFields()
    {
        var p = ScheduleParametersParser.Parse(ParetoParametersJson);

        p.ParetoDimension.Should().Be("Brand");
        p.ParetoMetric.Should().Be("Profit");
        p.IncludeVat.Should().BeTrue();
        p.ExcludeNegativeAmounts.Should().BeFalse();
        p.ClassAThreshold.Should().Be(70);
        p.ProfitBasis.Should().Be((int)ParetoProfitBasis.AverageCost);
        p.TimezoneOffsetMinutes.Should().Be(-120);
        p.StoreCodes.Should().BeEquivalentTo(new[] { "S01", "S02" });

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter!.Suppliers.Ids.Should().ContainSingle().Which.Should().Be("42");
    }

    // Mirrors collectChartParams() in Charts.cshtml: showOthers is "1"/"0" and the items key
    // is "itemsSelection" (not itemsSelectionJson). A scheduled chart previously ran Average Basket.
    private const string ChartParametersJson =
        "{\"mode\":\"Sales\",\"dimension\":\"Brand\",\"metric\":\"Value\",\"topN\":\"15\"," +
        "\"chartType\":\"bar\",\"showOthers\":\"0\",\"compareLastYear\":\"1\",\"includeVat\":\"1\"," +
        "\"storeCodes\":\"S01\"," +
        "\"itemsSelection\":\"{\\\"categories\\\":{\\\"ids\\\":[\\\"7\\\"],\\\"mode\\\":\\\"include\\\"}}\"}";

    [Fact]
    public void Parse_ReadsChartFields()
    {
        var p = ScheduleParametersParser.Parse(ChartParametersJson);

        p.ChartMode.Should().Be("Sales");
        p.ChartDimension.Should().Be("Brand");
        p.ChartMetric.Should().Be("Value");
        p.TopN.Should().Be(15);
        p.ChartType.Should().Be("bar");
        p.ShowOthers.Should().BeFalse("showOthers came as \"0\"");
        p.CompareLastYear.Should().BeTrue("compareLastYear came as \"1\"");

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter!.Categories.Ids.Should().ContainSingle().Which.Should().Be("7");
    }
}
