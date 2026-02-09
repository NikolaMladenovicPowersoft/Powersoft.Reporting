using FluentAssertions;
using Powersoft.Reporting.Core.Models;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class ReportFilterValidationTests
{
    #region Date Validation Tests

    [Fact]
    public void IsValid_Should_Return_True_For_Valid_Filter()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2024, 1, 1),
            DateTo = new DateTime(2024, 12, 31),
            PageNumber = 1,
            PageSize = 50
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void IsValid_Should_Fail_When_DateFrom_After_DateTo()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2024, 12, 31),
            DateTo = new DateTime(2024, 1, 1)
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Date From cannot be after Date To"));
    }

    [Fact]
    public void IsValid_Should_Fail_When_DateRange_Exceeds_Maximum()
    {
        // Arrange - more than 3 years (1095 days)
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2020, 1, 1),
            DateTo = new DateTime(2024, 1, 1)
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("1095 days"));
    }

    [Fact]
    public void IsValid_Should_Pass_For_Maximum_Allowed_Range()
    {
        // Arrange - exactly 3 years
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2021, 1, 1),
            DateTo = new DateTime(2023, 12, 31)
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_Should_Fail_When_DateTo_In_Future()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = DateTime.Today,
            DateTo = DateTime.Today.AddDays(10)
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("future"));
    }

    [Fact]
    public void IsValid_Should_Fail_When_DateFrom_Before_Year_2000()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(1999, 12, 31),
            DateTo = DateTime.Today
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("before year 2000"));
    }

    [Fact]
    public void IsValid_Should_Allow_Today_As_DateTo()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = DateTime.Today.AddDays(-30),
            DateTo = DateTime.Today
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeTrue();
    }

    #endregion

    #region Pagination Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void IsValid_Should_Fail_When_PageNumber_Less_Than_One(int pageNumber)
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = DateTime.Today.AddDays(-30),
            DateTo = DateTime.Today,
            PageNumber = pageNumber
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Page number"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(5000)]
    public void IsValid_Should_Fail_When_PageSize_Out_Of_Range(int pageSize)
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = DateTime.Today.AddDays(-30),
            DateTo = DateTime.Today,
            PageSize = pageSize
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Page size"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    public void IsValid_Should_Pass_For_Valid_PageSize(int pageSize)
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = DateTime.Today.AddDays(-30),
            DateTo = DateTime.Today,
            PageSize = pageSize
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeTrue();
    }

    #endregion

    #region Multiple Validation Errors Tests

    [Fact]
    public void IsValid_Should_Return_All_Errors()
    {
        // Arrange - multiple issues
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(1990, 1, 1), // Before 2000
            DateTo = DateTime.Today.AddDays(10), // In future
            PageNumber = 0, // Invalid
            PageSize = 0 // Invalid
        };

        // Act
        var isValid = filter.IsValid(out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Count.Should().BeGreaterThan(1);
    }

    #endregion

    #region Skip Calculation Tests

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 50, 100)]
    [InlineData(1, 25, 0)]
    [InlineData(5, 100, 400)]
    public void Skip_Should_Calculate_Correctly(int pageNumber, int pageSize, int expectedSkip)
    {
        // Arrange
        var filter = new ReportFilter
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        // Assert
        filter.Skip.Should().Be(expectedSkip);
    }

    #endregion

    #region IValidatableObject Tests

    [Fact]
    public void Validate_Should_Return_ValidationResults_For_Invalid_Filter()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2024, 12, 31),
            DateTo = new DateTime(2024, 1, 1)
        };

        var context = new ValidationContext(filter);
        
        // Act
        var results = filter.Validate(context).ToList();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.ErrorMessage!.Contains("Date From cannot be after Date To"));
    }

    [Fact]
    public void Validate_Should_Return_Empty_For_Valid_Filter()
    {
        // Arrange
        var filter = new ReportFilter
        {
            DateFrom = new DateTime(2024, 1, 1),
            DateTo = new DateTime(2024, 6, 30)
        };

        var context = new ValidationContext(filter);
        
        // Act
        var results = filter.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty();
    }

    #endregion
}
