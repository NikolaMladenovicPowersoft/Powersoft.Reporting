using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Tests.SalesThrough;

/// <summary>
/// Sell-through / mix formulas verified against the Splash workbook
/// (ERES SS26 ST Report - 19.06.xls, "SS26 SALES BY LINE" sheet):
///   Sell-through Qty %  = Sales Qty / Net Purchases Qty * 100
///   0 intake with sales = 100% (workbook row: purch 0, sold 1, % = 100)
/// </summary>
public class SalesThroughRowTests
{
    [Fact]
    public void SellThroughQtyPct_HalfSold_Is50()
    {
        var r = new SalesThroughRow { IntakeQty = 2, SalesQty = 1 };
        r.SellThroughQtyPct.Should().Be(50m);
    }

    [Fact]
    public void SellThroughQtyPct_AllSold_Is100()
    {
        var r = new SalesThroughRow { IntakeQty = 1, SalesQty = 1 };
        r.SellThroughQtyPct.Should().Be(100m);
    }

    [Fact]
    public void SellThroughQtyPct_ZeroIntake_Is100_WorkbookConvention()
    {
        // DUP sheet row: Net Purchases 0, Sales Qty 1, "%" column shows 100.
        var r = new SalesThroughRow { IntakeQty = 0, SalesQty = 1 };
        r.SellThroughQtyPct.Should().Be(100m);
    }

    [Fact]
    public void SellThroughValuePct_MatchesWorkbookExample()
    {
        // Workbook row 4: Net Purchases CV 106.5, Sales Net 164.69 -> 154.64 (rounded to 2 dp).
        var r = new SalesThroughRow { IntakeValue = 106.5m, SalesNet = 164.69m };
        r.SellThroughValuePct.Should().Be(154.64m);
    }

    [Fact]
    public void SellThroughPct_RoundsToTwoDecimals()
    {
        var r = new SalesThroughRow { IntakeQty = 3, SalesQty = 1 };
        r.SellThroughQtyPct.Should().Be(33.33m);
    }
}

public class SalesThroughFilterTests
{
    private static SalesThroughFilter Valid() => new()
    {
        DateFrom = new DateTime(2026, 1, 1),
        DateTo = new DateTime(2026, 6, 30)
    };

    [Fact]
    public void IsValid_AcceptsDefaultFilter()
    {
        Valid().IsValid(out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void IsValid_RejectsInvertedDates()
    {
        var f = Valid();
        f.DateFrom = new DateTime(2026, 7, 1);
        f.DateTo = new DateTime(2026, 1, 1);
        f.IsValid(out var errors).Should().BeFalse();
        errors.Should().ContainMatch("*Date From cannot be after Date To*");
    }

    [Fact]
    public void IsValid_RejectsRangeOverThreeYears()
    {
        var f = Valid();
        f.DateFrom = new DateTime(2020, 1, 1);
        f.DateTo = new DateTime(2026, 1, 1);
        f.IsValid(out var errors).Should().BeFalse();
        errors.Should().ContainMatch("*3 years*");
    }

    [Fact]
    public void IsValid_RejectsDuplicateGroups()
    {
        var f = Valid();
        f.PrimaryGroup = PsGroupBy.Model;
        f.SecondaryGroup = PsGroupBy.Model;
        f.IsValid(out var errors).Should().BeFalse();
        errors.Should().ContainMatch("*must be different*");
    }

    [Fact]
    public void IsValid_RejectsThirdGroupDuplicatingPrimary()
    {
        var f = Valid();
        f.PrimaryGroup = PsGroupBy.Category;
        f.SecondaryGroup = PsGroupBy.Model;
        f.ThirdGroup = PsGroupBy.Category;
        f.IsValid(out var errors).Should().BeFalse();
    }

    [Fact]
    public void IsSummary_RequiresPrimaryGroup()
    {
        var f = Valid();
        f.Summary = true;
        f.IsSummary.Should().BeFalse();

        f.PrimaryGroup = PsGroupBy.Category;
        f.IsSummary.Should().BeTrue();
    }
}
