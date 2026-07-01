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

    // ─────────────────────────────────────────────────────────────────────────────
    // Template-authoring UI contract (Views/TemplateAdmin/Index.cshtml).
    //
    // The authoring UI builds ParametersJson from hard-coded option lists. If any option
    // value is not a real enum member, ParseEnum/Enum.TryParse silently falls back to the
    // default — a template that *looks* grouped by "Brand" would run ungrouped. These tests
    // pin every option list to its enum so a typo in the cshtml fails here instead of on a
    // customer's scheduled report. Keep these arrays byte-identical to the cshtml.
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Ui_PsGroups =
        { "None","Category","Department","Brand","Season","Supplier","Store","Model","Colour","Size","GroupSize","Fabric","Franchise","User","PaymentType" };
    private static readonly string[] Ui_AbGroups =
        { "None","Store","Category","Department","Brand","Season","Customer","User","Supplier","Model","Colour","Size","CustomerCategory1","CustomerCategory2","Item","GroupSize","Fabric" };
    private static readonly string[] Ui_CatGroups =
        { "None","Category","Department","Supplier","Brand","Season","Store","Model","Colour","Size","Customer","Agent","PaymentType","Franchise","User" };
    private static readonly string[] Ui_ParetoDims =
        { "Category","Department","Brand","Season","Supplier","Store","Model","Colour","Size","GroupSize","Fabric","Customer","CustomerCategory1","CustomerCategory2","User","Item" };
    private static readonly string[] Ui_ChartDims =
        { "Category","Store","Brand","Customer","Supplier","Department","Season","Agent","User","Model","Colour","Item" };

    public static IEnumerable<object[]> AuthoringOptionLists()
    {
        foreach (var v in Ui_PsGroups)     yield return new object[] { typeof(PsGroupBy), v };
        foreach (var v in Ui_AbGroups)     yield return new object[] { typeof(GroupByType), v };
        foreach (var v in Ui_CatGroups)    yield return new object[] { typeof(CatalogueGroupBy), v };
        foreach (var v in Ui_ParetoDims)   yield return new object[] { typeof(ParetoDimension), v };
        foreach (var v in Ui_ChartDims)    yield return new object[] { typeof(ChartDimension), v };
        foreach (var v in new[] { "Detailed","Summary","Monthly" })                                         yield return new object[] { typeof(PsReportMode), v };
        foreach (var v in new[] { "Daily","Weekly","Monthly" })                                             yield return new object[] { typeof(BreakdownType), v };
        foreach (var v in new[] { "Summary","Detailed" })                                                   yield return new object[] { typeof(CatalogueReportMode), v };
        foreach (var v in new[] { "Sale","Purchase","Both" })                                               yield return new object[] { typeof(CatalogueReportOn), v };
        foreach (var v in new[] { "Value","Quantity","Profit" })                                            yield return new object[] { typeof(ParetoMetric), v };
        foreach (var v in new[] { "Sales","SalesVsReturns","Purchases","PurchasesVsReturns","SalesVsPurchases" }) yield return new object[] { typeof(ChartMode), v };
        foreach (var v in new[] { "Value","Quantity","Count" })                                             yield return new object[] { typeof(ChartMetric), v };
    }

    [Theory]
    [MemberData(nameof(AuthoringOptionLists))]
    public void AuthoringUi_EveryOptionValue_IsAValidEnumMember(Type enumType, string optionValue)
    {
        Enum.TryParse(enumType, optionValue, ignoreCase: true, out var parsed).Should().BeTrue(
            "authoring option '{0}' must be a real {1} member or the scheduler silently drops the filter",
            optionValue, enumType.Name);
        Enum.IsDefined(enumType, parsed!).Should().BeTrue();
    }

    // The whole point of the Pareto authoring default: it must NOT resolve to Item, whose
    // unfiltered item-level scan can time out on large tenants (see reporting-engine rule #10).
    [Fact]
    public void AuthoringUi_ParetoDefaultJson_ResolvesToSafeCategory_NotItem()
    {
        // Exactly what buildParams() emits for a freshly-added Pareto item with defaults.
        var json = "{\"dimension\":\"Category\",\"metric\":\"Value\",\"includeVat\":false," +
                   "\"reportDateRange\":{\"type\":\"LastMonth\",\"value\":0}}";

        var p = ScheduleParametersParser.Parse(json);

        p.ParetoDimension.Should().Be("Category");
        var dim = Enum.TryParse<ParetoDimension>(p.ParetoDimension, true, out var d) ? d : ParetoDimension.Item;
        dim.Should().Be(ParetoDimension.Category);
        dim.Should().NotBe(ParetoDimension.Item, "the authoring default must avoid the timeout-prone Item scan");
    }

    // Catalogue authoring default must resolve to a grouped Summary, not the heavy ungrouped
    // Detailed item listing the scheduler falls back to when primaryGroup is absent.
    [Fact]
    public void AuthoringUi_CatalogueDefaultJson_ResolvesToGroupedSummary()
    {
        var json = "{\"reportMode\":\"Summary\",\"reportOn\":\"Sale\",\"primaryGroup\":\"Category\"," +
                   "\"secondaryGroup\":\"None\",\"thirdGroup\":\"None\",\"showProfit\":false,\"showStock\":false," +
                   "\"reportDateRange\":{\"type\":\"LastMonth\",\"value\":0}}";

        var p = ScheduleParametersParser.Parse(json);

        Enum.TryParse<CatalogueReportMode>(p.CatReportMode, true, out var rm).Should().BeTrue();
        rm.Should().Be(CatalogueReportMode.Summary);
        Enum.TryParse<CatalogueReportOn>(p.CatReportOn, true, out var ro).Should().BeTrue();
        ro.Should().Be(CatalogueReportOn.Sale);
        Enum.TryParse<CatalogueGroupBy>(p.CatPrimaryGroup, true, out var pg).Should().BeTrue();
        pg.Should().Be(CatalogueGroupBy.Category);
        pg.Should().NotBe(CatalogueGroupBy.None, "an ungrouped Detailed catalogue lists every item and can be slow");
    }

    [Fact]
    public void AuthoringUi_ChartsDefaultJson_ResolvesModeDimensionMetric()
    {
        var json = "{\"mode\":\"Sales\",\"dimension\":\"Category\",\"metric\":\"Value\"," +
                   "\"chartType\":\"pie\",\"includeVat\":false," +
                   "\"reportDateRange\":{\"type\":\"LastMonth\",\"value\":0}}";

        var p = ScheduleParametersParser.Parse(json);

        Enum.TryParse<ChartMode>(p.ChartMode, true, out var m).Should().BeTrue();
        m.Should().Be(ChartMode.Sales);
        Enum.TryParse<ChartDimension>(p.ChartDimension, true, out var dim).Should().BeTrue();
        dim.Should().Be(ChartDimension.Category);
        Enum.TryParse<ChartMetric>(p.ChartMetric, true, out var met).Should().BeTrue();
        met.Should().Be(ChartMetric.Value);
        p.ChartType.Should().Be("pie");
    }
}
