using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class ItemRepository : IItemRepository
{
    private readonly string _connectionString;

    public ItemRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Item>> SearchItemsAsync(string? search, bool includeInactive = false, int maxResults = 200)
    {
        var items = new List<Item>();
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var searchTerm = hasSearch ? $"%{search.Trim()}%" : null;

        var sql = @"
            SELECT TOP (@MaxResults) pk_ItemID, ItemCode, ItemNamePrimary, ItemNameSecondary, ISNULL(ItemActive, 1)
            FROM tbl_Item
            WHERE (@Search IS NULL OR ItemCode LIKE @Search OR ItemNamePrimary LIKE @Search OR ISNULL(ItemNameSecondary,'') LIKE @Search)
              AND (@IncludeInactive = 1 OR ISNULL(ItemActive, 1) = 1)
            ORDER BY ItemCode";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive ? 1 : 0);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new Item
            {
                ItemId = (int)reader.GetInt64(0),
                ItemCode = reader.GetString(1),
                ItemNamePrimary = reader.GetString(2),
                ItemNameSecondary = reader.IsDBNull(3) ? null : reader.GetString(3),
                Active = reader.IsDBNull(4) || reader.GetBoolean(4)
            });
        }

        return items;
    }
}
