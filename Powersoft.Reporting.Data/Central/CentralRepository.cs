using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Central;

public class CentralRepository
{
    private readonly string _connectionString;

    public CentralRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Company>> GetActiveCompaniesAsync()
    {
        var companies = new List<Company>();
        
        const string sql = @"
            SELECT pk_CompanyCode, CompanyName, CompanyActive, 
                   Address1, Address2, Phone, Email
            FROM tbl_Company 
            WHERE CompanyActive = 1 
            ORDER BY CompanyName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            companies.Add(new Company
            {
                CompanyCode = reader.GetString(0),
                CompanyName = reader.GetString(1),
                CompanyActive = reader.GetBoolean(2),
                Address1 = reader.IsDBNull(3) ? null : reader.GetString(3),
                Address2 = reader.IsDBNull(4) ? null : reader.GetString(4),
                Phone = reader.IsDBNull(5) ? null : reader.GetString(5),
                Email = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        
        return companies;
    }

    public async Task<List<Database>> GetActiveDatabasesForCompanyAsync(string companyCode)
    {
        var databases = new List<Database>();
        
        const string sql = @"
            SELECT t1.pk_DBCode, t1.DBFriendlyName, t1.DBName, 
                   t1.DBServerID, t1.DBProviderInstanceName,
                   t1.DBUserName, t1.DBPassword, t1.DBActive,
                   t2.pk_CompanyCode, t2.CompanyName
            FROM tbl_DB t1
            INNER JOIN tbl_Company t2 ON t1.fk_CompanyCode = t2.pk_CompanyCode
            WHERE t2.pk_CompanyCode = @CompanyCode 
              AND t1.DBActive = 1 
              AND t2.CompanyActive = 1
            ORDER BY t1.DBFriendlyName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CompanyCode", companyCode);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            databases.Add(new Database
            {
                DBCode = reader.GetString(0),
                DBFriendlyName = reader.GetString(1),
                DBName = reader.GetString(2),
                DBServerID = reader.GetString(3),
                DBProviderInstanceName = reader.IsDBNull(4) ? null : reader.GetString(4),
                DBUserName = reader.IsDBNull(5) ? null : reader.GetString(5),
                DBPassword = reader.IsDBNull(6) ? null : reader.GetString(6),
                DBActive = reader.GetBoolean(7),
                CompanyCode = reader.GetString(8),
                CompanyName = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        
        return databases;
    }

    public async Task<Database?> GetDatabaseByCodeAsync(string dbCode)
    {
        const string sql = @"
            SELECT t1.pk_DBCode, t1.DBFriendlyName, t1.DBName, 
                   t1.DBServerID, t1.DBProviderInstanceName,
                   t1.DBUserName, t1.DBPassword, t1.DBActive,
                   t2.pk_CompanyCode, t2.CompanyName
            FROM tbl_DB t1
            INNER JOIN tbl_Company t2 ON t1.fk_CompanyCode = t2.pk_CompanyCode
            WHERE t1.pk_DBCode = @DBCode";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DBCode", dbCode);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return new Database
            {
                DBCode = reader.GetString(0),
                DBFriendlyName = reader.GetString(1),
                DBName = reader.GetString(2),
                DBServerID = reader.GetString(3),
                DBProviderInstanceName = reader.IsDBNull(4) ? null : reader.GetString(4),
                DBUserName = reader.IsDBNull(5) ? null : reader.GetString(5),
                DBPassword = reader.IsDBNull(6) ? null : reader.GetString(6),
                DBActive = reader.GetBoolean(7),
                CompanyCode = reader.GetString(8),
                CompanyName = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
        }
        
        return null;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
