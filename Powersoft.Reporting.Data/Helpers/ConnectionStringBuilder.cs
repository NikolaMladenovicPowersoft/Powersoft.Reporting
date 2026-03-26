using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Helpers;

public static class ConnectionStringBuilder
{
    /// <summary>
    /// Build connection string from DB record fields (local dev / direct connectivity).
    /// </summary>
    public static string Build(Database db)
    {
        var sb = new StringBuilder();
        
        sb.Append("Data Source=");
        sb.Append(db.DBServerID);
        
        if (!string.IsNullOrWhiteSpace(db.DBProviderInstanceName))
        {
            sb.Append('\\');
            sb.Append(db.DBProviderInstanceName);
        }
        
        sb.Append(";Initial Catalog=");
        sb.Append(db.DBName);
        
        if (!string.IsNullOrWhiteSpace(db.DBUserName))
        {
            sb.Append(";User ID=");
            sb.Append(db.DBUserName);
        }
        
        if (!string.IsNullOrWhiteSpace(db.DBPassword))
        {
            sb.Append(";Password=");
            sb.Append(Cryptography.Decrypt(db.DBPassword));
        }
        
        sb.Append(";Pooling=true");
        sb.Append(";MultipleActiveResultSets=True");
        sb.Append(";TrustServerCertificate=True");
        sb.Append(";Encrypt=False");
        
        return sb.ToString();
    }

    /// <summary>
    /// Build tenant connection string by reusing server/credentials from a reference
    /// connection string (e.g. PSCentral from appsettings.json) and only swapping
    /// the Initial Catalog to the tenant DB name. Use when the deployment server
    /// cannot reach the DBServerID stored in tbl_DB (private IPs).
    /// </summary>
    public static string BuildFromReference(Database db, string referenceConnectionString)
    {
        var refBuilder = new SqlConnectionStringBuilder(referenceConnectionString);
        refBuilder.InitialCatalog = db.DBName;
        
        if (!refBuilder.ConnectionString.Contains("MultipleActiveResultSets", StringComparison.OrdinalIgnoreCase))
            refBuilder.MultipleActiveResultSets = true;
        if (!refBuilder.ConnectionString.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
            refBuilder.TrustServerCertificate = true;
        
        return refBuilder.ConnectionString;
    }
}
