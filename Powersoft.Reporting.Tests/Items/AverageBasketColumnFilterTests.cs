using FluentAssertions;
using Powersoft.Reporting.Data.Tenant;
using Xunit;

namespace Powersoft.Reporting.Tests.Items;

// Covers p0-avgbasket-fix item #3: the grid exposes "Sale Value" / "Ret Value" column
// filters, but the repository ColumnSqlMap had no matching key so those filters were
// silently dropped. These tests pin the mapping and the VAT-aware resolution that keeps
// a column filter consistent with the value shown in the grid.
public class AverageBasketColumnFilterTests
{
    [Theory]
    [InlineData("SaleValue")]
    [InlineData("ReturnValue")]
    [InlineData("Sales")]
    [InlineData("AvgBasket")]
    [InlineData("Invoices")]
    public void ColumnSqlMap_contains_grid_filterable_columns(string column)
    {
        AverageBasketRepository.ColumnSqlMap.Should().ContainKey(column);
    }

    [Fact]
    public void SaleValue_and_ReturnValue_map_to_net_base_without_vat()
    {
        AverageBasketRepository.ColumnSqlMap["SaleValue"].Should().Be("d.NetSales");
        AverageBasketRepository.ColumnSqlMap["ReturnValue"].Should().Be("d.NetReturns");
    }

    [Fact]
    public void ApplyVat_returns_base_when_vat_off()
    {
        AverageBasketRepository.ApplyVatToValueExpr("SaleValue", "d.NetSales", includeVat: false)
            .Should().Be("d.NetSales");
        AverageBasketRepository.ApplyVatToValueExpr("ReturnValue", "d.NetReturns", includeVat: false)
            .Should().Be("d.NetReturns");
    }

    [Fact]
    public void ApplyVat_adds_vat_for_value_columns_when_vat_on()
    {
        AverageBasketRepository.ApplyVatToValueExpr("SaleValue", "d.NetSales", includeVat: true)
            .Should().Be("(d.NetSales + d.VatSales)");
        AverageBasketRepository.ApplyVatToValueExpr("ReturnValue", "d.NetReturns", includeVat: true)
            .Should().Be("(d.NetReturns + d.VatReturns)");
        AverageBasketRepository.ApplyVatToValueExpr("Sales", "(d.NetSales - d.NetReturns)", includeVat: true)
            .Should().Be("(d.NetSales + d.VatSales - d.NetReturns - d.VatReturns)");
    }

    [Fact]
    public void ApplyVat_is_case_insensitive_on_column_name()
    {
        AverageBasketRepository.ApplyVatToValueExpr("salevalue", "d.NetSales", includeVat: true)
            .Should().Be("(d.NetSales + d.VatSales)");
    }

    [Fact]
    public void ApplyVat_leaves_non_value_columns_untouched()
    {
        AverageBasketRepository.ApplyVatToValueExpr("QtySold", "d.QtySold", includeVat: true)
            .Should().Be("d.QtySold");
    }
}
