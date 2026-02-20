using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;

namespace Powersoft.Reporting.Data.Tenant;

public class IniRepository : IIniRepository
{
    private readonly string _connectionString;

    public IniRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Dictionary<string, string>> GetLayoutAsync(string moduleCode, string headerCode, string userCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
            SELECT d.ParmCode, d.ParmValue
            FROM tbl_IniDetail d
            INNER JOIN tbl_IniHeader h ON d.fk_IniHeaderID = h.pk_IniHeaderID
            WHERE h.fk_IniModuleCode = @ModuleCode
              AND h.IniHeaderCode = @HeaderCode
              AND ISNULL(h.fk_UserCode, 'ALL') = @UserCode";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
        cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
        cmd.Parameters.AddWithValue("@UserCode", userCode);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = reader.GetString(0);
            var value = reader.GetString(1);
            result[code] = value;
        }

        return result;
    }

    public async Task SaveLayoutAsync(string moduleCode, string headerCode, string headerDescription,
        string userCode, Dictionary<string, string> parameters)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            // Step 1: Find or create header (matches legacy RAS_AddBulkIniDetail)
            const string headerSql = @"
                DECLARE @HeaderID bigint = 0;

                SELECT @HeaderID = pk_IniHeaderID
                FROM tbl_IniHeader
                WHERE fk_IniModuleCode = @ModuleCode
                  AND IniHeaderCode = @HeaderCode
                  AND ISNULL(fk_UserCode, 'ALL') = @UserCode
                  AND fk_StoreCode IS NULL;

                IF ISNULL(@HeaderID, 0) = 0
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM tbl_IniModule WHERE pk_IniModuleCode = @ModuleCode)
                        INSERT INTO tbl_IniModule (pk_IniModuleCode, IniModuleDesc)
                        VALUES (@ModuleCode, @HeaderDescr);

                    INSERT INTO tbl_IniHeader WITH (ROWLOCK)
                        (fk_IniModuleCode, IniHeaderCode, IniHeaderDescr, fk_UserCode, fk_StoreCode,
                         CreatedBy, LastModifiedBy, LastModifiedDateTime)
                    VALUES
                        (@ModuleCode, @HeaderCode, @HeaderDescr, @UserCode, NULL,
                         @UserCode, @UserCode, GETUTCDATE());

                    SET @HeaderID = SCOPE_IDENTITY();
                END
                ELSE
                BEGIN
                    UPDATE tbl_IniHeader WITH (ROWLOCK)
                    SET LastModifiedBy = @UserCode,
                        LastModifiedDateTime = GETUTCDATE()
                    WHERE pk_IniHeaderID = @HeaderID;
                END

                DELETE FROM tbl_IniDetail WITH (ROWLOCK) WHERE fk_IniHeaderID = @HeaderID;
                SELECT @HeaderID AS HeaderID;";

            long headerId;
            using (var cmd = new SqlCommand(headerSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
                cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
                cmd.Parameters.AddWithValue("@HeaderDescr", headerDescription);
                cmd.Parameters.AddWithValue("@UserCode", userCode);

                var result = await cmd.ExecuteScalarAsync();
                headerId = Convert.ToInt64(result);
            }

            if (headerId <= 0 || parameters.Count == 0)
            {
                transaction.Commit();
                return;
            }

            // Step 2: Bulk insert details (same transaction — atomic with delete)
            using var insertCmd = new SqlCommand();
            insertCmd.Connection = conn;
            insertCmd.Transaction = transaction;

            var sb = new System.Text.StringBuilder();
            sb.Append("INSERT INTO tbl_IniDetail (fk_IniHeaderID, ParmCode, ParmValue) VALUES ");

            int i = 0;
            foreach (var kvp in parameters)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"(@HeaderID, @Code{i}, @Val{i})");
                insertCmd.Parameters.AddWithValue($"@Code{i}", kvp.Key);
                insertCmd.Parameters.AddWithValue($"@Val{i}", kvp.Value ?? "");
                i++;
            }

            insertCmd.Parameters.AddWithValue("@HeaderID", headerId);
            insertCmd.CommandText = sb.ToString();

            await insertCmd.ExecuteNonQueryAsync();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> DeleteLayoutAsync(string moduleCode, string headerCode, string userCode)
    {
        const string sql = @"
            DECLARE @HeaderID bigint = 0;

            SELECT @HeaderID = pk_IniHeaderID
            FROM tbl_IniHeader
            WHERE fk_IniModuleCode = @ModuleCode
              AND IniHeaderCode = @HeaderCode
              AND ISNULL(fk_UserCode, 'ALL') = @UserCode
              AND fk_StoreCode IS NULL;

            IF ISNULL(@HeaderID, 0) > 0
            BEGIN
                DELETE FROM tbl_IniDetail WHERE fk_IniHeaderID = @HeaderID;
                DELETE FROM tbl_IniHeader WHERE pk_IniHeaderID = @HeaderID;
                SELECT 1 AS Deleted;
            END
            ELSE
                SELECT 0 AS Deleted;";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
        cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
        cmd.Parameters.AddWithValue("@UserCode", userCode);

        await conn.OpenAsync();
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 1;
    }
}
