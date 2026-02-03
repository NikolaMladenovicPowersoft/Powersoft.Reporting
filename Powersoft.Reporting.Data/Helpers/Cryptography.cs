using System.Security.Cryptography;
using System.Text;

namespace Powersoft.Reporting.Data.Helpers;

/// <summary>
/// Port of Powersoft.CloudCommon.Security.Cryptography for password decryption.
/// </summary>
public static class Cryptography
{
    private static string Initialize()
    {
        // ASCII 100-120: defghijklmnopqrstuvwx
        var sb = new StringBuilder();
        for (int i = 100; i <= 120; i++)
            sb.Append((char)i);
        return sb.ToString();
    }

    private static string Clear()
    {
        // VB.NET bug: For i = 120 To 100 without Step -1 doesn't execute!
        // So Clear() returns empty string in the original code
        return "";
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            var s = new ASCIIEncoding();
            
            // Must match legacy VB.NET behavior - no iterations/hash specified
            #pragma warning disable SYSLIB0041
            using var pdb = new Rfc2898DeriveBytes(Clear(), s.GetBytes(Initialize()));
            #pragma warning restore SYSLIB0041

            using var aes = Aes.Create();
            aes.Key = pdb.GetBytes(32);
            aes.IV = pdb.GetBytes(16);

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(cipherBytes, 0, cipherBytes.Length);
            }
            return Encoding.Unicode.GetString(ms.ToArray());
        }
        catch
        {
            // If decryption fails, return original (might be plaintext)
            return cipherText;
        }
    }

    public static string Encrypt(string clearText)
    {
        if (string.IsNullOrEmpty(clearText))
            return string.Empty;

        byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
        var s = new ASCIIEncoding();
        
        #pragma warning disable SYSLIB0041
        using var pdb = new Rfc2898DeriveBytes(Clear(), s.GetBytes(Initialize()));
        #pragma warning restore SYSLIB0041

        using var aes = Aes.Create();
        aes.Key = pdb.GetBytes(32);
        aes.IV = pdb.GetBytes(16);

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(clearBytes, 0, clearBytes.Length);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public static string DecryptPasswordInConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        int pwdIndex = connectionString.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
        if (pwdIndex < 0)
            return connectionString;

        int start = pwdIndex + 9;
        int end = connectionString.IndexOf(';', pwdIndex);
        if (end < 0) end = connectionString.Length;

        string encryptedPwd = connectionString.Substring(start, end - start);
        string decryptedPwd = Decrypt(encryptedPwd);

        return connectionString.Replace(encryptedPwd, decryptedPwd);
    }
}
