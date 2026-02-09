using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class StoreRepository : IStoreRepository
{
    private readonly string _connectionString;

    public StoreRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Store>> GetActiveStoresAsync()
    {
        var stores = new List<Store>();
        
        const string sql = @"
            SELECT pk_StoreCode, StoreName, ShortName, Active
            FROM tbl_Store
            WHERE Active = 1
            ORDER BY StoreName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            stores.Add(new Store
            {
                StoreCode = reader.GetString(0),
                StoreName = reader.GetString(1),
                ShortName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Active = reader.GetBoolean(3)
            });
        }
        
        return stores;
    }

    public async Task<List<Store>> GetStoresByCodesAsync(IEnumerable<string> storeCodes)
    {
        if (!storeCodes.Any())
            return new List<Store>();

        var stores = new List<Store>();
        var codes = storeCodes.ToList();
        var paramNames = codes.Select((_, i) => $"@p{i}").ToList();
        
        var sql = $@"
            SELECT pk_StoreCode, StoreName, ShortName, Active
            FROM tbl_Store
            WHERE pk_StoreCode IN ({string.Join(", ", paramNames)})
            ORDER BY StoreName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        
        for (int i = 0; i < codes.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", codes[i]);
        }
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            stores.Add(new Store
            {
                StoreCode = reader.GetString(0),
                StoreName = reader.GetString(1),
                ShortName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Active = reader.GetBoolean(3)
            });
        }
        
        return stores;
    }
}
