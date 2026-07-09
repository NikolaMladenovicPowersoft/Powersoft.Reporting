using Powersoft.Reporting.Core.Helpers;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Tests.CashFlow;

public class CashFlowStatementBuilderTests
{
    private static CashFlowResult SampleResult() => new()
    {
        Categories = new List<CashFlowMappingCategory>
        {
            new() { GroupName = "Operating Activities - Cash In", GroupSortOrder = 1000, CategoryName = "Customers", CategorySortOrder = 1100 },
            new() { GroupName = "Operating Activities - Cash In", GroupSortOrder = 1000, CategoryName = "Subsidies", CategorySortOrder = 1300 },
            new() { GroupName = "Bank", GroupSortOrder = 6000, CategoryName = "Cash A/C", CategorySortOrder = 6000 }
        },
        Rows = new List<CashFlowRow>
        {
            new() { AccountCode = "122001", AccountName = "DEBTOR A", GroupName = "Operating Activities - Cash In", GroupSortOrder = 1000, CategoryName = "Customers", CategorySortOrder = 1100, Amount = 150m, PriorAmount = 100m },
            new() { AccountCode = "411001", AccountName = "SALES", GroupName = "Operating Activities - Cash In", GroupSortOrder = 1000, CategoryName = "Customers", CategorySortOrder = 1100, Amount = 50m },
            new() { AccountCode = "124001", AccountName = "CASH", GroupName = "Bank", GroupSortOrder = 6000, CategoryName = "Cash A/C", CategorySortOrder = 6000, IsBank = true, Amount = -200m },
            new() { AccountCode = "510001", AccountName = "SUSPENSE", GroupName = "", CategoryName = "", Amount = 10m }
        }
    };

    [Fact]
    public void Build_EmptyCategory_RenderedAsSkeletonLine()
    {
        var statement = CashFlowStatementBuilder.Build(SampleResult(), new CashFlowFilter());

        var opIn = statement.Groups.First(g => g.Name == "Operating Activities - Cash In");
        var subsidies = opIn.Categories.Single(c => c.Name == "Subsidies");
        Assert.True(subsidies.IsEmpty);
        Assert.Equal(0m, subsidies.Amount);
    }

    [Fact]
    public void Build_UnmappedAccounts_SurfaceInTrailingUnassignedGroup()
    {
        var statement = CashFlowStatementBuilder.Build(SampleResult(), new CashFlowFilter());

        var last = statement.Groups.Last();
        Assert.Equal(CashFlowStatementBuilder.UnassignedName, last.Name);
        Assert.Equal(10m, last.Amount);
    }

    [Fact]
    public void Build_GroupTotals_AreSumOfCategories_AndNetIsMinusBank()
    {
        var statement = CashFlowStatementBuilder.Build(SampleResult(), new CashFlowFilter());

        var opIn = statement.Groups.First(g => g.Name == "Operating Activities - Cash In");
        Assert.Equal(200m, opIn.Amount);
        Assert.Equal(100m, opIn.PriorAmount);
        Assert.Equal(200m, statement.NetCashMovement); // -(bank -200)
        Assert.Equal(210m, statement.TotalIn);         // non-bank positive rows: 150 + 50 + 10
        Assert.Equal(0m, statement.TotalOut);          // no non-bank negative rows in the sample
    }

    [Fact]
    public void Build_ShowAccounts_TogglesAccountRows()
    {
        var hidden = CashFlowStatementBuilder.Build(SampleResult(), new CashFlowFilter { ShowAccounts = false });
        var shown = CashFlowStatementBuilder.Build(SampleResult(), new CashFlowFilter { ShowAccounts = true });

        var hiddenCustomers = hidden.Groups.First().Categories.Single(c => c.Name == "Customers");
        var shownCustomers = shown.Groups.First().Categories.Single(c => c.Name == "Customers");
        Assert.Empty(hiddenCustomers.Accounts);
        Assert.Equal(2, shownCustomers.Accounts.Count);
        Assert.Equal("DEBTOR A", shownCustomers.Accounts[0].AccountName); // sorted by name
    }

    [Fact]
    public void Build_Monthly_SumsPerMonthAndNetPerMonth()
    {
        var result = SampleResult();
        result.Months = new List<string> { "2025-01", "2025-02" };
        result.Rows[0].MonthAmounts = new Dictionary<string, decimal> { ["2025-01"] = 90m, ["2025-02"] = 60m };
        result.Rows[1].MonthAmounts = new Dictionary<string, decimal> { ["2025-01"] = 50m };
        result.Rows[2].MonthAmounts = new Dictionary<string, decimal> { ["2025-01"] = -140m, ["2025-02"] = -60m };
        result.Rows[3].MonthAmounts = new Dictionary<string, decimal> { ["2025-02"] = 10m };

        var statement = CashFlowStatementBuilder.Build(result, new CashFlowFilter { Monthly = true });

        var customers = statement.Groups.First().Categories.Single(c => c.Name == "Customers");
        Assert.Equal(140m, customers.MonthAmounts!["2025-01"]);
        Assert.Equal(60m, customers.MonthAmounts["2025-02"]);
        Assert.Equal(140m, statement.MonthNetCashMovement["2025-01"]); // -(bank -140)
        Assert.Equal(60m, statement.MonthNetCashMovement["2025-02"]);
    }

    [Fact]
    public void Filter_Monthly_LimitedToTwelveMonths()
    {
        var ok = new CashFlowFilter { DateFrom = new DateTime(2025, 1, 1), DateTo = new DateTime(2025, 12, 31), Monthly = true };
        var tooLong = new CashFlowFilter { DateFrom = new DateTime(2024, 12, 1), DateTo = new DateTime(2025, 12, 31), Monthly = true };
        Assert.True(ok.IsValid());
        Assert.False(tooLong.IsValid());
    }
}
