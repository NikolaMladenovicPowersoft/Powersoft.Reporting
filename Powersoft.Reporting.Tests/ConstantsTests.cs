using FluentAssertions;
using Powersoft.Reporting.Core.Constants;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class ConstantsTests
{
    #region SessionKeys Tests

    [Fact]
    public void SessionKeys_TenantConnectionString_Should_Be_Defined()
    {
        // Assert
        SessionKeys.TenantConnectionString.Should().NotBeNullOrEmpty();
        SessionKeys.TenantConnectionString.Should().Be("TenantConnectionString");
    }

    [Fact]
    public void SessionKeys_ConnectedDatabase_Should_Be_Defined()
    {
        // Assert
        SessionKeys.ConnectedDatabase.Should().NotBeNullOrEmpty();
        SessionKeys.ConnectedDatabase.Should().Be("ConnectedDatabase");
    }

    [Fact]
    public void SessionKeys_ConnectedDatabaseCode_Should_Be_Defined()
    {
        // Assert
        SessionKeys.ConnectedDatabaseCode.Should().NotBeNullOrEmpty();
        SessionKeys.ConnectedDatabaseCode.Should().Be("ConnectedDatabaseCode");
    }

    [Fact]
    public void SessionKeys_Should_Have_Unique_Values()
    {
        // Arrange
        var keys = new[]
        {
            SessionKeys.TenantConnectionString,
            SessionKeys.ConnectedDatabase,
            SessionKeys.ConnectedDatabaseCode
        };

        // Assert - all keys should be unique
        keys.Distinct().Count().Should().Be(keys.Length);
    }

    [Fact]
    public void SessionKeys_Should_Not_Contain_Spaces()
    {
        // Assert
        SessionKeys.TenantConnectionString.Should().NotContain(" ");
        SessionKeys.ConnectedDatabase.Should().NotContain(" ");
        SessionKeys.ConnectedDatabaseCode.Should().NotContain(" ");
    }

    #endregion
}
