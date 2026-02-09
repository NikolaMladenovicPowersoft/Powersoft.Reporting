using FluentAssertions;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Data.Auth;
using Powersoft.Reporting.Data.Central;
using Powersoft.Reporting.Data.Tenant;
using System.Reflection;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class InterfaceContractTests
{
    #region ICentralRepository Contract Tests

    [Fact]
    public void CentralRepository_Should_Implement_ICentralRepository()
    {
        // Assert
        typeof(CentralRepository).Should().Implement<ICentralRepository>();
    }

    [Fact]
    public void ICentralRepository_Should_Have_Required_Methods()
    {
        // Arrange
        var interfaceType = typeof(ICentralRepository);

        // Assert
        interfaceType.GetMethod("GetActiveCompaniesAsync").Should().NotBeNull();
        interfaceType.GetMethod("GetActiveDatabasesForCompanyAsync").Should().NotBeNull();
        interfaceType.GetMethod("GetDatabaseByCodeAsync").Should().NotBeNull();
        interfaceType.GetMethod("TestConnectionAsync").Should().NotBeNull();
    }

    #endregion

    #region IStoreRepository Contract Tests

    [Fact]
    public void StoreRepository_Should_Implement_IStoreRepository()
    {
        // Assert
        typeof(StoreRepository).Should().Implement<IStoreRepository>();
    }

    [Fact]
    public void IStoreRepository_Should_Have_Required_Methods()
    {
        // Arrange
        var interfaceType = typeof(IStoreRepository);

        // Assert
        interfaceType.GetMethod("GetActiveStoresAsync").Should().NotBeNull();
        interfaceType.GetMethod("GetStoresByCodesAsync").Should().NotBeNull();
    }

    #endregion

    #region IAverageBasketRepository Contract Tests

    [Fact]
    public void AverageBasketRepository_Should_Implement_IAverageBasketRepository()
    {
        // Assert
        typeof(AverageBasketRepository).Should().Implement<IAverageBasketRepository>();
    }

    [Fact]
    public void IAverageBasketRepository_Should_Have_Required_Methods()
    {
        // Arrange
        var interfaceType = typeof(IAverageBasketRepository);

        // Assert
        interfaceType.GetMethods().Should().Contain(m => m.Name == "GetAverageBasketDataAsync");
        interfaceType.GetMethod("TestConnectionAsync").Should().NotBeNull();
    }

    #endregion

    #region IAuthenticationService Contract Tests

    [Fact]
    public void AuthenticationService_Should_Implement_IAuthenticationService()
    {
        // Assert
        typeof(AuthenticationService).Should().Implement<Core.Interfaces.IAuthenticationService>();
    }

    [Fact]
    public void IAuthenticationService_Should_Have_Required_Methods()
    {
        // Arrange
        var interfaceType = typeof(Core.Interfaces.IAuthenticationService);

        // Assert
        interfaceType.GetMethod("AuthenticateAsync").Should().NotBeNull();
        interfaceType.GetMethod("GetUserByUsernameAsync").Should().NotBeNull();
    }

    #endregion

    #region ITenantRepositoryFactory Contract Tests

    [Fact]
    public void ITenantRepositoryFactory_Should_Have_Required_Methods()
    {
        // Arrange
        var interfaceType = typeof(ITenantRepositoryFactory);

        // Assert
        interfaceType.GetMethod("CreateStoreRepository").Should().NotBeNull();
        interfaceType.GetMethod("CreateAverageBasketRepository").Should().NotBeNull();
    }

    #endregion

    #region Return Type Tests

    [Fact]
    public void All_Repository_Methods_Should_Return_Task()
    {
        // Arrange
        var repoInterfaces = new[]
        {
            typeof(ICentralRepository),
            typeof(IStoreRepository),
            typeof(IAverageBasketRepository),
            typeof(Core.Interfaces.IAuthenticationService)
        };

        // Assert
        foreach (var interfaceType in repoInterfaces)
        {
            var methods = interfaceType.GetMethods();
            foreach (var method in methods)
            {
                // All async methods should return Task or Task<T>
                if (method.Name.EndsWith("Async"))
                {
                    var returnType = method.ReturnType;
                    var isTask = returnType == typeof(System.Threading.Tasks.Task) ||
                                 (returnType.IsGenericType && 
                                  returnType.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>));
                    
                    isTask.Should().BeTrue($"Method {interfaceType.Name}.{method.Name} should return Task");
                }
            }
        }
    }

    #endregion
}
