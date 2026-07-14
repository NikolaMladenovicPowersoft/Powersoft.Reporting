using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Tests.Catalogue;

/// <summary>
/// George (2026-07): reports must show the tenant-defined attribute name (tbl_Field.FieldDesc,
/// e.g. "GENDER") instead of the generic "Attribute 1". These tests cover the fallback logic
/// used by exports, print preview and the grid.
/// </summary>
public class AttributeCaptionTests
{
    [Fact]
    public void AttrCaption_ReturnsTenantName_WhenDefined()
    {
        var filter = new CatalogueFilter
        {
            AttributeCaptions = new Dictionary<int, string> { [1] = "GENDER", [3] = "AGE GROUP" }
        };

        filter.AttrCaption(1, "Attr 1").Should().Be("GENDER");
        filter.AttrCaption(3, "Attr 3").Should().Be("AGE GROUP");
    }

    [Fact]
    public void AttrCaption_FallsBackToGenericLabel_WhenMissingOrBlank()
    {
        var filter = new CatalogueFilter
        {
            AttributeCaptions = new Dictionary<int, string> { [2] = "  " }
        };

        filter.AttrCaption(1, "Attr 1").Should().Be("Attr 1", because: "index 1 has no caption");
        filter.AttrCaption(2, "Attr 2").Should().Be("Attr 2", because: "whitespace caption must not be used");
    }

    [Fact]
    public void GroupLabel_ResolvesItemAttrGroups_ToTenantName()
    {
        var filter = new CatalogueFilter
        {
            AttributeCaptions = new Dictionary<int, string> { [1] = "GENDER" }
        };

        filter.GroupLabel(CatalogueGroupBy.ItemAttr1).Should().Be("GENDER");
        filter.GroupLabel(CatalogueGroupBy.ItemAttr2).Should().Be("Attribute 2",
            because: "no caption defined for attribute 2");
        filter.GroupLabel(CatalogueGroupBy.Brand).Should().Be("Brand",
            because: "non-attribute groups keep the enum name");
    }
}
