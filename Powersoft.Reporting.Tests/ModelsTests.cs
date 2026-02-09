using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class ModelsTests
{
    #region AverageBasketRow Tests

    [Fact]
    public void AverageBasketRow_CYTotalTransactions_Should_Calculate_Correctly()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYInvoiceCount = 100,
            CYCreditCount = 10
        };
        
        // Assert
        row.CYTotalTransactions.Should().Be(90);
    }

    [Fact]
    public void AverageBasketRow_CYTotalQty_Should_Calculate_Correctly()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYQtySold = 500,
            CYQtyReturned = 50
        };
        
        // Assert
        row.CYTotalQty.Should().Be(450);
    }

    [Fact]
    public void AverageBasketRow_CYAverageNet_Should_Calculate_Correctly()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYInvoiceCount = 100,
            CYCreditCount = 0,
            CYNetSales = 10000,
            CYNetReturns = 0
        };
        
        // Assert
        row.CYAverageNet.Should().Be(100);
    }

    [Fact]
    public void AverageBasketRow_CYAverageNet_Should_Return_Zero_When_No_Transactions()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYInvoiceCount = 0,
            CYCreditCount = 0,
            CYNetSales = 0,
            CYNetReturns = 0
        };
        
        // Assert
        row.CYAverageNet.Should().Be(0);
    }

    [Fact]
    public void AverageBasketRow_YoYChangePercent_Should_Calculate_Correctly()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYNetSales = 1200,
            CYNetReturns = 0,
            LYTotalNet = 1000
        };
        
        // Assert
        row.YoYChangePercent.Should().Be(20); // 20% increase
    }

    [Fact]
    public void AverageBasketRow_YoYChangePercent_Should_Return_100_When_No_LY_Data()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYNetSales = 1000,
            CYNetReturns = 0,
            LYTotalNet = 0
        };
        
        // Assert
        row.YoYChangePercent.Should().Be(100);
    }

    [Fact]
    public void AverageBasketRow_YoYChangePercent_Should_Return_Zero_When_Both_Zero()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYNetSales = 0,
            CYNetReturns = 0,
            LYTotalNet = 0
        };
        
        // Assert
        row.YoYChangePercent.Should().Be(0);
    }

    [Fact]
    public void AverageBasketRow_Negative_YoY_Should_Calculate_Correctly()
    {
        // Arrange
        var row = new AverageBasketRow
        {
            CYNetSales = 800,
            CYNetReturns = 0,
            LYTotalNet = 1000
        };
        
        // Assert
        row.YoYChangePercent.Should().Be(-20); // 20% decrease
    }

    #endregion

    #region ReportFilter Tests

    [Fact]
    public void ReportFilter_Default_Values_Should_Be_Set()
    {
        // Arrange
        var filter = new ReportFilter();
        
        // Assert
        filter.Breakdown.Should().Be(BreakdownType.Monthly);
        filter.GroupBy.Should().Be(GroupByType.None);
        filter.IncludeVat.Should().BeFalse();
        filter.CompareLastYear.Should().BeFalse();
        filter.PageNumber.Should().Be(1);
        filter.PageSize.Should().Be(50);
        filter.StoreCodes.Should().BeEmpty();
    }

    [Fact]
    public void ReportFilter_Skip_Should_Calculate_Correctly()
    {
        // Arrange
        var filter = new ReportFilter
        {
            PageNumber = 3,
            PageSize = 50
        };
        
        // Assert
        filter.Skip.Should().Be(100); // (3-1) * 50
    }

    [Fact]
    public void ReportFilter_HasStoreFilter_Should_Return_True_When_Stores_Selected()
    {
        // Arrange
        var filter = new ReportFilter
        {
            StoreCodes = new List<string> { "001", "002" }
        };
        
        // Assert
        filter.HasStoreFilter.Should().BeTrue();
    }

    [Fact]
    public void ReportFilter_HasStoreFilter_Should_Return_False_When_No_Stores()
    {
        // Arrange
        var filter = new ReportFilter();
        
        // Assert
        filter.HasStoreFilter.Should().BeFalse();
    }

    #endregion

    #region PagedResult Tests

    [Fact]
    public void PagedResult_TotalPages_Should_Calculate_Correctly()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            TotalCount = 150,
            PageSize = 50
        };
        
        // Assert
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public void PagedResult_TotalPages_Should_Round_Up()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            TotalCount = 151,
            PageSize = 50
        };
        
        // Assert
        result.TotalPages.Should().Be(4);
    }

    [Fact]
    public void PagedResult_HasPreviousPage_Should_Return_False_On_First_Page()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            PageNumber = 1,
            TotalCount = 100,
            PageSize = 50
        };
        
        // Assert
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_HasPreviousPage_Should_Return_True_On_Second_Page()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            PageNumber = 2,
            TotalCount = 100,
            PageSize = 50
        };
        
        // Assert
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_HasNextPage_Should_Return_False_On_Last_Page()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            PageNumber = 2,
            TotalCount = 100,
            PageSize = 50
        };
        
        // Assert
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_HasNextPage_Should_Return_True_When_More_Pages()
    {
        // Arrange
        var result = new PagedResult<string>
        {
            PageNumber = 1,
            TotalCount = 100,
            PageSize = 50
        };
        
        // Assert
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_Empty_Should_Create_Valid_Empty_Result()
    {
        // Act
        var result = PagedResult<string>.Empty(2, 25);
        
        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(25);
        result.TotalPages.Should().Be(0);
    }

    #endregion

    #region Store Tests

    [Fact]
    public void Store_DisplayName_Should_Use_ShortName_When_Available()
    {
        // Arrange
        var store = new Store
        {
            StoreCode = "001",
            StoreName = "Main Store",
            ShortName = "MS"
        };
        
        // Assert
        store.DisplayName.Should().Be("MS - Main Store");
    }

    [Fact]
    public void Store_DisplayName_Should_Use_Code_When_No_ShortName()
    {
        // Arrange
        var store = new Store
        {
            StoreCode = "001",
            StoreName = "Main Store",
            ShortName = null
        };
        
        // Assert
        store.DisplayName.Should().Be("001 - Main Store");
    }

    #endregion
}
