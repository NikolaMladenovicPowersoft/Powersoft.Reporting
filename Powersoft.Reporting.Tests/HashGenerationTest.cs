using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Powersoft.Reporting.Tests;

// Run HashGenerationTest, then: SELECT UserPassword FROM tbl_User WHERE pk_UserCode = 'REPORTING_TEST'
public class HashGenerationTest
{
    [Fact]
    public void Generate_Test123_Hash_For_Verification()
    {
        const string password = "Test123!";
        
        var utf8Hash = GenerateSha1Utf8(password);
        var unicodeHash = GenerateSha1Unicode(password);
        
        // Output for manual comparison with DB
        System.Console.WriteLine($"Password: {password}");
        System.Console.WriteLine($"UTF-8 hash:   {utf8Hash}");
        System.Console.WriteLine($"Unicode hash: {unicodeHash}");
        System.Console.WriteLine("---");
        System.Console.WriteLine("Run in SQL: SELECT UserPassword FROM tbl_User WHERE pk_UserCode = 'REPORTING_TEST'");
        System.Console.WriteLine("Compare the DB value (trimmed) with one of the hashes above.");
        
        // At least one hash should be 40 chars (SHA1 hex)
        Assert.Equal(40, utf8Hash.Length);
        Assert.Equal(40, unicodeHash.Length);
    }
    
    private static string GenerateSha1Utf8(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }
    
    private static string GenerateSha1Unicode(string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
