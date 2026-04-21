using System.Reflection;
using FluentAssertions;
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
    public void Slugify_TruncatesAt60Chars_AndDoesNotEndWithHyphen()
    {
        var longName = new string('a', 200);
        var slug = Slug(longName);
        slug.Length.Should().BeLessThanOrEqualTo(60);
        slug.Should().NotEndWith("-");
    }

    [Fact]
    public void Slugify_IsIdempotent()
    {
        var first = Slug("Quarterly Stock Report — 2026");
        var second = Slug(first);
        second.Should().Be(first, because: "running slugify on a slug must be a no-op");
    }
}
