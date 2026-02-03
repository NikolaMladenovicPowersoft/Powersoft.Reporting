using System.Text;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Helpers;

public static class ConnectionStringBuilder
{
    public static string Build(Database db)
    {
        var sb = new StringBuilder();
        
        // Data Source
        sb.Append("Data Source=");
        sb.Append(db.DBServerID);
        
        if (!string.IsNullOrWhiteSpace(db.DBProviderInstanceName))
        {
            sb.Append('\\');
            sb.Append(db.DBProviderInstanceName);
        }
        
        // Initial Catalog
        sb.Append(";Initial Catalog=");
        sb.Append(db.DBName);
        
        // Credentials
        if (!string.IsNullOrWhiteSpace(db.DBUserName))
        {
            sb.Append(";User ID=");
            sb.Append(db.DBUserName);
        }
        
        if (!string.IsNullOrWhiteSpace(db.DBPassword))
        {
            sb.Append(";Password=");
            // Decrypt the password
            sb.Append(Cryptography.Decrypt(db.DBPassword));
        }
        
        // Connection options
        sb.Append(";Pooling=true");
        sb.Append(";MultipleActiveResultSets=True");
        sb.Append(";TrustServerCertificate=True");
        
        return sb.ToString();
    }
}
