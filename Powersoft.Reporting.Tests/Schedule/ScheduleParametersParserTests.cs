using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
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

    [Fact]
    public void Parse_CancelLog_RootDateFromTo_FallbackWhenNoReportDateRange()
    {
        var json = "{\"dateFrom\":\"2026-01-01\",\"dateTo\":\"2026-06-30\",\"clReportType\":\"Detailed\",\"actionType\":\"All\"}";
        var p = ScheduleParametersParser.Parse(json);

        p.DateRange.Should().NotBeNull();
        p.DateRange!.Type.Should().Be(ReportDateRangeType.Custom);
        p.DateRange.DateFrom.Should().Be("2026-01-01");
        p.DateRange.DateTo.Should().Be("2026-06-30");
    }

    // Mirrors collectCatalogueParams() in Catalogue.cshtml. GetEnumSelectList() emits numeric enum
    // values, so reportMode/primaryGroup/etc. arrive as numeric strings ("1","2"). Before the
    // Catalogue scheduler handler was added, a scheduled Catalogue silently ran Average Basket.
    // The reportMode/primaryGroup keys collide with PS, so they are captured into distinct Cat* fields.
    private const string CatalogueParametersJson =
        "{\"dateFrom\":\"2026-01-01\",\"dateTo\":\"2026-01-31\",\"dateBasis\":\"1\",\"useDateTime\":true," +
        "\"reportMode\":\"1\",\"reportOn\":\"0\",\"primaryGroup\":\"2\",\"secondaryGroup\":\"5\",\"thirdGroup\":\"0\"," +
        "\"profitBasedOn\":\"88\",\"profitIncludesVat\":true,\"stockValueBasedOn\":\"99\",\"stockValueIncludesVat\":false," +
        "\"costType\":\"98\",\"displayColumns\":\"ItemCode,ItemName,Quantity,Value\"," +
        "\"showProfit\":true,\"showStock\":false,\"storeCodes\":\"S01\"," +
        "\"itemsSelection\":\"{\\\"categories\\\":{\\\"ids\\\":[\\\"7\\\"],\\\"mode\\\":\\\"include\\\"}}\"," +
        "\"sortColumn\":\"ItemCode\",\"sortDirection\":\"ASC\"," +
        "\"columnFilters\":\"{\\\"values\\\":{\\\"ItemCode\\\":\\\"AB\\\"},\\\"operators\\\":{\\\"ItemCode\\\":\\\"contains\\\"}}\"}";

    [Fact]
    public void Parse_ReadsCatalogueFields()
    {
        var p = ScheduleParametersParser.Parse(CatalogueParametersJson);

        p.CatReportMode.Should().Be("1");
        p.CatReportOn.Should().Be("0");
        p.CatPrimaryGroup.Should().Be("2");
        p.CatSecondaryGroup.Should().Be("5");
        p.CatThirdGroup.Should().Be("0");
        p.CatProfitBasedOn.Should().Be(88);
        p.CatProfitIncludesVat.Should().BeTrue();
        p.CatStockValueBasedOn.Should().Be(99);
        p.CatStockValueIncludesVat.Should().BeFalse();
        p.CatCostType.Should().Be(98);
        p.CatDateBasis.Should().Be("1");
        p.CatUseDateTime.Should().BeTrue();
        p.CatDisplayColumns.Should().Be("ItemCode,ItemName,Quantity,Value");
        p.CatColumnFilters.Should().NotBeNullOrWhiteSpace();
        p.ShowProfit.Should().BeTrue();
        p.ShowStock.Should().BeFalse();
        p.StoreCodes.Should().BeEquivalentTo(new[] { "S01" });

        var filter = ItemsSelectionParser.Parse(p.ItemsSelectionJson);
        filter!.Categories.Ids.Should().ContainSingle().Which.Should().Be("7");
    }

    // Mirrors collectScheduleParameters() in TrialBalance.cshtml. Before the TB scheduler handler
    // was added, a scheduled Trial Balance would have dropped mode/zero-movement/header selections.
    private const string TrialBalanceParametersJson =
        "{\"reportDateRange\":{\"type\":\"AsAt\",\"value\":0}," +
        "\"tbReportMode\":\"Summary\",\"tbIncludeZeroMovements\":true," +
        "\"tbSelectedAccounts\":\"1001,1002\",\"tbSelectedHeaders\":\"10,20\"," +
        "\"tbSuppressedHeaders\":\"30\",\"reportType\":\"TrialBalance\"}";

    [Fact]
    public void Parse_ReadsTrialBalanceFields()
    {
        var p = ScheduleParametersParser.Parse(TrialBalanceParametersJson);

        p.TbReportMode.Should().Be("Summary");
        p.TbIncludeZeroMovements.Should().BeTrue();
        p.TbSelectedAccounts.Should().Be("1001,1002");
        p.TbSelectedHeaders.Should().Be("10,20");
        p.TbSuppressedHeaders.Should().Be("30");

        Enum.TryParse<TrialBalanceReportMode>(p.TbReportMode, true, out var rm).Should().BeTrue();
        rm.Should().Be(TrialBalanceReportMode.Summary);
    }

    // The handler relies on Enum.TryParse turning these numeric strings into the right members.
    [Fact]
    public void Parse_CatalogueEnumStrings_RoundTripToEnumMembers()
    {
        var p = ScheduleParametersParser.Parse(CatalogueParametersJson);

        Enum.TryParse<CatalogueReportMode>(p.CatReportMode, true, out var rm).Should().BeTrue();
        rm.Should().Be(CatalogueReportMode.Summary);

        Enum.TryParse<CatalogueGroupBy>(p.CatPrimaryGroup, true, out var pg).Should().BeTrue();
        pg.Should().Be(CatalogueGroupBy.Category);

        Enum.TryParse<CatalogueGroupBy>(p.CatSecondaryGroup, true, out var sg).Should().BeTrue();
        sg.Should().Be(CatalogueGroupBy.Brand);
    }
}
