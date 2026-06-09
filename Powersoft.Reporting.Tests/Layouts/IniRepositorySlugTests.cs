using System.Reflection;
using FluentAssertions;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Data.Tenant;
using Xunit;

namespace Powersoft.Reporting.Tests.Layouts;

/// <summary>
/// Locks in the slug shape for named-layout header codes. The slug becomes part of the
/// IniHeaderCode (e.g. "CATALOGUE:my-layout") so its stability matters: changing it
/// would orphan all previously saved layouts.
///
/// If you need to change slug behaviour, you must also write a one-shot data migration.
/// </summary>
public class IniRepositorySlugTests
{
    private static string Slug(string s)
    {
        var m = typeof(IniRepository).GetMethod("SlugifyLayoutName",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SlugifyLayoutName not found");
        return (string)m.Invoke(null, new object[] { s })!;
    }

    [Theory]
    [InlineData("My Layout",          "my-layout")]
    [InlineData("Monthly Sales",      "monthly-sales")]
    [InlineData("  trim me  ",        "trim-me")]
    [InlineData("UPPER CASE",         "upper-case")]
    [InlineData("dots.and,commas",    "dots-and-commas")]
    [InlineData("multi   spaces",     "multi-spaces")]
    [InlineData("---leading---",      "leading")]
    [InlineData("ćžšđč",              "layout")] // non-ASCII collapses to nothing -> fallback
    [InlineData("",                   "layout")]
    [InlineData("   ",                "layout")]
    [InlineData("a",                  "a")]
    [InlineData("123",                "123")]
    public void Slugify_KnownInputs_ProduceStableOutputs(string input, string expected)
    {
        Slug(input).Should().Be(expected);
    }

    [Fact]
    public void Slugify_LongInput_ProducesValidSlug()
    {
        var longName = new string('a', 200);
        var slug = Slug(longName);
        slug.Should().NotBeNullOrEmpty();
        slug.Should().NotEndWith("-");
    }

    [Fact]
    public void BuildNamedHeaderCode_FitsWithin20Chars()
    {
        var longName = new string('a', 200);
        var slug = Slug(longName);
        var m = typeof(IniRepository).GetMethod("BuildNamedHeaderCode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildNamedHeaderCode not found");
        var headerCode = (string)m.Invoke(null, new object[] { "AVGBASKET", slug })!;
        headerCode.Length.Should().BeLessThanOrEqualTo(20,
            because: "IniHeaderCode column is nvarchar(20)");
        headerCode.Should().StartWith("AVGBASKET:");
        headerCode.Should().NotEndWith("-");
    }

    [Fact]
    public void BelowMinStock_SlugConstant_IsPresent()
    {
        ModuleConstants.IniHeaderBelowMinStock.Should().Be("BELOWMIN");
        ModuleConstants.IniDescriptionBelowMinStock.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TrialBalance_SlugConstant_IsPresent()
    {
        ModuleConstants.IniHeaderTrialBalance.Should().Be("TRIALBAL");
        ModuleConstants.IniHeaderTrialBalance.Length.Should().BeLessThanOrEqualTo(20,
            because: "IniHeaderCode column is nvarchar(20) and the slug suffix needs room");
        ModuleConstants.IniDescriptionTrialBalance.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TrialBalance_NamedHeaderCode_FitsWithin20Chars()
    {
        var longName = new string('a', 200);
        var slug = Slug(longName);
        var m = typeof(IniRepository).GetMethod("BuildNamedHeaderCode",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildNamedHeaderCode not found");
        var headerCode = (string)m.Invoke(null, new object[] { ModuleConstants.IniHeaderTrialBalance, slug })!;
        headerCode.Length.Should().BeLessThanOrEqualTo(20);
        headerCode.Should().StartWith("TRIALBAL:");
    }

    [Fact]
    public void ProfitLoss_SlugConstant_IsPresent()
    {
        ModuleConstants.IniHeaderProfitLoss.Should().Be("PROFITLOSS");
        ModuleConstants.IniHeaderProfitLoss.Length.Should().BeLessThanOrEqualTo(20,
            because: "IniHeaderCode column is nvarchar(20) and the slug suffix needs room");
        ModuleConstants.IniDescriptionProfitLoss.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ProfitLoss_NamedHeaderCode_FitsWithin20Chars()
    {
        var longName = new string('a', 200);
        var slug = Slug(longName);
        var m = typeof(IniRepository).GetMethod("BuildNamedHeaderCode",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildNamedHeaderCode not found");
        var headerCode = (string)m.Invoke(null, new object[] { ModuleConstants.IniHeaderProfitLoss, slug })!;
        headerCode.Length.Should().BeLessThanOrEqualTo(20);
        headerCode.Should().StartWith("PROFITLOSS:");
        headerCode.Should().NotEndWith("-");
    }

    [Fact]
    public void Slugify_IsIdempotent()
    {
        var first = Slug("Quarterly Stock Report — 2026");
        var second = Slug(first);
        second.Should().Be(first, because: "running slugify on a slug must be a no-op");
    }
}
