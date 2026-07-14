using FluentAssertions;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Tests.CashFlow;

/// <summary>
/// Validation rules for the Cash Flow Mapping admin editor (George, 2026-07-14: admins manage
/// COA ranges directly). Matching is an ordinal string comparison, so validation must use the
/// same comparison — not numeric.
/// </summary>
public class CashFlowMappingEntryTests
{
    private static CashFlowMappingEntry Valid() => new()
    {
        GroupName = "Operating Activities - Cash In",
        GroupSortOrder = 1000,
        CategoryName = "Customers",
        CategorySortOrder = 1100,
        CodeFrom = "411",
        CodeTo = "411999"
    };

    [Fact]
    public void Validate_AcceptsWellFormedEntry()
    {
        Valid().Validate().Should().BeNull();
    }

    [Fact]
    public void Validate_TrimsInputs()
    {
        var e = Valid();
        e.GroupName = "  Bank  ";
        e.CodeFrom = " 124001 ";
        e.CodeTo = " 124001 ";
        e.Validate().Should().BeNull();
        e.GroupName.Should().Be("Bank");
        e.CodeFrom.Should().Be("124001");
    }

    [Theory]
    [InlineData("", "Group name is required.")]
    [InlineData("   ", "Group name is required.")]
    public void Validate_RequiresGroupName(string group, string expected)
    {
        var e = Valid();
        e.GroupName = group;
        e.Validate().Should().Be(expected);
    }

    [Fact]
    public void Validate_RequiresCategoryName()
    {
        var e = Valid();
        e.CategoryName = "";
        e.Validate().Should().Be("Category name is required.");
    }

    [Fact]
    public void Validate_RequiresCodes()
    {
        var e1 = Valid(); e1.CodeFrom = "";
        e1.Validate().Should().Be("Code From is required.");

        var e2 = Valid(); e2.CodeTo = " ";
        e2.Validate().Should().Be("Code To is required.");
    }

    [Fact]
    public void Validate_EnforcesColumnLengths()
    {
        var e1 = Valid(); e1.GroupName = new string('X', 61);
        e1.Validate().Should().Contain("60 characters");

        var e2 = Valid(); e2.CodeFrom = new string('1', 21);
        e2.Validate().Should().Contain("20 characters");
    }

    [Fact]
    public void Validate_RejectsRangeThatCanNeverMatch_OrdinalComparison()
    {
        var e = Valid();
        e.CodeFrom = "412";
        e.CodeTo = "411999";
        e.Validate().Should().Contain("never match");
    }

    [Fact]
    public void Validate_SingleAccountRange_FromEqualsTo_IsValid()
    {
        var e = Valid();
        e.CodeFrom = "124001";
        e.CodeTo = "124001";
        e.Validate().Should().BeNull();
    }

    [Fact]
    public void Validate_UsesStringOrdering_NotNumeric()
    {
        // As TEXT "43" <= "439999" even though numerically 43 < 439999 too; but "9" > "10" as text.
        // A range From="9" To="10" is numerically fine yet can never match as text — must be rejected.
        var e = Valid();
        e.CodeFrom = "9";
        e.CodeTo = "10";
        e.Validate().Should().Contain("never match");
    }
}
