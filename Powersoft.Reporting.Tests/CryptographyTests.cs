using FluentAssertions;
using Powersoft.Reporting.Data.Helpers;
using Xunit;

namespace Powersoft.Reporting.Tests;

public class CryptographyTests
{
    [Fact]
    public void Encrypt_And_Decrypt_Should_Return_Original_Text()
    {
        // Arrange
        var originalText = "TestPassword123!";
        
        // Act
        var encrypted = Cryptography.Encrypt(originalText);
        var decrypted = Cryptography.Decrypt(encrypted);
        
        // Assert
        encrypted.Should().NotBe(originalText);
        encrypted.Should().NotBeNullOrEmpty();
        decrypted.Should().Be(originalText);
    }

    [Fact]
    public void Decrypt_Empty_String_Should_Return_Empty()
    {
        // Act
        var result = Cryptography.Decrypt("");
        
        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Decrypt_Null_Should_Return_Empty()
    {
        // Act
        var result = Cryptography.Decrypt(null!);
        
        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Decrypt_Invalid_Base64_Should_Return_Original()
    {
        // Arrange - not valid base64
        var invalidCipherText = "not-valid-base64!!!";
        
        // Act
        var result = Cryptography.Decrypt(invalidCipherText);
        
        // Assert - should return original when decryption fails
        result.Should().Be(invalidCipherText);
    }

    [Fact]
    public void Encrypt_Empty_String_Should_Return_Empty()
    {
        // Act
        var result = Cryptography.Encrypt("");
        
        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DecryptPasswordInConnectionString_Should_Decrypt_Password_Part()
    {
        // Arrange
        var password = "MySecretPass";
        var encryptedPassword = Cryptography.Encrypt(password);
        var connectionString = $"Server=localhost;Database=TestDB;User ID=sa;Password={encryptedPassword}";
        
        // Act
        var result = Cryptography.DecryptPasswordInConnectionString(connectionString);
        
        // Assert
        result.Should().Contain($"Password={password}");
        result.Should().Contain("Server=localhost");
        result.Should().Contain("Database=TestDB");
    }

    [Fact]
    public void DecryptPasswordInConnectionString_Without_Password_Should_Return_Original()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=TestDB;Integrated Security=true";
        
        // Act
        var result = Cryptography.DecryptPasswordInConnectionString(connectionString);
        
        // Assert
        result.Should().Be(connectionString);
    }

    [Fact]
    public void DecryptPasswordInConnectionString_Empty_Should_Return_Empty()
    {
        // Act
        var result = Cryptography.DecryptPasswordInConnectionString("");
        
        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with spaces and special !@#$%")]
    [InlineData("unicode: Ср士日本語")]
    [InlineData("very-long-password-that-exceeds-normal-length-expectations-1234567890")]
    public void Encrypt_Decrypt_Various_Passwords(string password)
    {
        // Act
        var encrypted = Cryptography.Encrypt(password);
        var decrypted = Cryptography.Decrypt(encrypted);
        
        // Assert
        decrypted.Should().Be(password);
    }
}
