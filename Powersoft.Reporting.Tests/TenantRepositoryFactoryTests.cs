using FluentAssertions;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Factories;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class TenantRepositoryFactoryTests
{
    private readonly ITenantRepositoryFactory _factory;

    public TenantRepositoryFactoryTests()
    {
        _factory = new TenantRepositoryFactory();
    }

    #region CreateStoreRepository Tests

    [Fact]
    public void CreateStoreRepository_Should_Return_Valid_Instance()
    {
        // Arrange
        var connectionString = "Server=test;Database=test;User Id=test;Password=test;";

        // Act
        var repo = _factory.CreateStoreRepository(connectionString);

        // Assert
        repo.Should().NotBeNull();
        repo.Should().BeAssignableTo<IStoreRepository>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateStoreRepository_Should_Throw_For_Invalid_ConnectionString(string? connectionString)
    {
        // Act
        var act = () => _factory.CreateStoreRepository(connectionString!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Connection string*");
    }

    [Fact]
    public void CreateStoreRepository_Should_Create_New_Instance_Each_Time()
    {
        // Arrange
        var connectionString = "Server=test;Database=test;";

        // Act
        var repo1 = _factory.CreateStoreRepository(connectionString);
        var repo2 = _factory.CreateStoreRepository(connectionString);

        // Assert
        repo1.Should().NotBeSameAs(repo2);
    }

    #endregion

    #region CreateAverageBasketRepository Tests

    [Fact]
    public void CreateAverageBasketRepository_Should_Return_Valid_Instance()
    {
        // Arrange
        var connectionString = "Server=test;Database=test;User Id=test;Password=test;";

        // Act
        var repo = _factory.CreateAverageBasketRepository(connectionString);

        // Assert
        repo.Should().NotBeNull();
        repo.Should().BeAssignableTo<IAverageBasketRepository>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateAverageBasketRepository_Should_Throw_For_Invalid_ConnectionString(string? connectionString)
    {
        // Act
        var act = () => _factory.CreateAverageBasketRepository(connectionString!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Connection string*");
    }

    [Fact]
    public void CreateAverageBasketRepository_Should_Create_New_Instance_Each_Time()
    {
        // Arrange
        var connectionString = "Server=test;Database=test;";

        // Act
        var repo1 = _factory.CreateAverageBasketRepository(connectionString);
        var repo2 = _factory.CreateAverageBasketRepository(connectionString);

        // Assert
        repo1.Should().NotBeSameAs(repo2);
    }

    #endregion

    #region Factory Pattern Tests

    [Fact]
    public void Factory_Should_Allow_Different_ConnectionStrings()
    {
        // Arrange
        var conn1 = "Server=server1;Database=db1;";
        var conn2 = "Server=server2;Database=db2;";

        // Act - should not throw
        var repo1 = _factory.CreateAverageBasketRepository(conn1);
        var repo2 = _factory.CreateAverageBasketRepository(conn2);

        // Assert
        repo1.Should().NotBeNull();
        repo2.Should().NotBeNull();
        repo1.Should().NotBeSameAs(repo2);
    }

    #endregion
}
