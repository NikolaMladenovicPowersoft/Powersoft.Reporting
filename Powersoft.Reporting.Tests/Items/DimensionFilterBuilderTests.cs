using FluentAssertions;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Tenant;
using Xunit;

namespace Powersoft.Reporting.Tests.Items;

/// <summary>
/// Regression coverage for the NULL handling of the Items Selection dimension filter.
///
/// Bug context: "exclude category X" used to emit a bare "col NOT IN (X)". In SQL,
/// NULL NOT IN (X) is UNKNOWN, so every uncategorised item was silently dropped. The
/// component lets the user pick the explicit N/A marker, which is the *only* thing that
/// should drop NULLs. These tests pin the corrected, internally-consistent semantics.
/// </summary>
public class DimensionFilterBuilderTests
{
    private static ItemsSelectionFilter WithCategory(FilterMode mode, params string[] ids)
    {
        return new ItemsSelectionFilter
        {
            Categories = new DimensionFilter { Mode = mode, Ids = ids.ToList() }
        };
    }

    [Fact]
    public void Exclude_realIds_only_keeps_NULL_rows()
    {
        var (sql, parms) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Exclude, "326"));

        sql.Should().Contain("NOT IN");
        sql.Should().Contain("IS NULL");
        sql.Should().NotContain("IS NOT NULL");
        parms.Should().ContainSingle();
    }

    [Fact]
    public void Exclude_realIds_plus_NA_drops_NULL_rows()
    {
        var (sql, _) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Exclude, "326", "__NA__"));

        sql.Should().Contain("NOT IN");
        sql.Should().Contain("IS NOT NULL");
    }

    [Fact]
    public void Exclude_NA_only_drops_NULL_rows()
    {
        var (sql, parms) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Exclude, "__NA__"));

        sql.Should().Contain("IS NOT NULL");
        sql.Should().NotContain("NOT IN");
        parms.Should().BeEmpty();
    }

    [Fact]
    public void Include_realIds_only_does_not_touch_NULL()
    {
        var (sql, _) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Include, "326"));

        sql.Should().Contain("IN");
        sql.Should().NotContain("IS NULL");
    }

    [Fact]
    public void Include_realIds_plus_NA_also_keeps_NULL()
    {
        var (sql, _) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Include, "326", "__NA__"));

        sql.Should().Contain("IN (");
        sql.Should().Contain("IS NULL");
        sql.Should().NotContain("IS NOT NULL");
    }

    [Fact]
    public void Include_NA_only_is_IS_NULL()
    {
        var (sql, parms) = DimensionFilterBuilder.Build(WithCategory(FilterMode.Include, "__NA__"));

        sql.Should().Contain("IS NULL");
        sql.Should().NotContain("NOT IN");
        parms.Should().BeEmpty();
    }

    [Fact]
    public void No_filter_emits_nothing()
    {
        var (sql, parms) = DimensionFilterBuilder.Build(new ItemsSelectionFilter());

        sql.Should().BeEmpty();
        parms.Should().BeEmpty();
    }
}
