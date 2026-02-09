using System.Security.Cryptography;
using System.Text;

namespace Powersoft.Reporting.Data.Helpers;

// Must match Powersoft.CloudCommon.Cryptography - used for tbl_DB password decryption
public static class Cryptography
{
    private static string Initialize()
    {
        var sb = new StringBuilder();
        for (int i = 100; i <= 120; i++)
            sb.Append((char)i);
        return sb.ToString();
    }

    private static string Clear()
    {
        // original VB loop For i=120 To 100 (no Step -1) never runs, returns ""
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
