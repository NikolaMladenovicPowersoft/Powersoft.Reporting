using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

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

    // ==================== Named / public layouts (multi per user) ====================

    /// <summary>
    /// Header code shape for named layouts: "{prefix}:{slug}".
    /// Plain "{prefix}" (no colon) is reserved for the legacy single default layout.
    /// </summary>
    private static string BuildNamedHeaderCode(string prefix, string slug) => $"{prefix}:{slug}";

    /// <summary>
    /// Slugifies a user-visible layout name into a stable short id used inside the header code.
    /// Lowercase ASCII alphanumeric + hyphen; collapses other characters; truncates at 60 chars.
    /// Empty/whitespace -> "layout".
    /// </summary>
    internal static string SlugifyLayoutName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "layout";
        var sb = new System.Text.StringBuilder(name.Length);
        bool prevHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9')
            {
                sb.Append(ch);
                prevHyphen = false;
            }
            else if (!prevHyphen && sb.Length > 0)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) slug = "layout";
        if (slug.Length > 60) slug = slug.Substring(0, 60).TrimEnd('-');
        return slug;
    }

    public async Task<IReadOnlyList<SavedLayoutInfo>> ListLayoutsAsync(
        string moduleCode, string headerCodePrefix, string userCode)
    {
        // Lists every header for this module whose code starts with the prefix and is visible
        // to the caller (own private OR public). Excludes private layouts of OTHER users.
        const string sql = @"
            SELECT
                h.IniHeaderCode,
                h.IniHeaderDescr,
                h.fk_UserCode,
                h.CreatedBy,
                h.LastModifiedDateTime
            FROM tbl_IniHeader h
            WHERE h.fk_IniModuleCode = @ModuleCode
              AND (h.IniHeaderCode = @PrefixExact OR h.IniHeaderCode LIKE @PrefixLike)
              AND (h.fk_UserCode IS NULL OR h.fk_UserCode = @UserCode)
              AND h.fk_StoreCode IS NULL
            ORDER BY
                CASE WHEN h.fk_UserCode IS NULL THEN 1 ELSE 0 END, -- own first, then public
                h.IniHeaderDescr";

        var list = new List<SavedLayoutInfo>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
        cmd.Parameters.AddWithValue("@PrefixExact", headerCodePrefix);
        cmd.Parameters.AddWithValue("@PrefixLike", headerCodePrefix + ":%");
        cmd.Parameters.AddWithValue("@UserCode", userCode);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var headerCode = reader.GetString(0);
            var name = reader.IsDBNull(1) ? headerCode : reader.GetString(1);
            var ownerCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            var createdBy = reader.IsDBNull(3) ? null : reader.GetString(3);
            var modified = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
            var isPublic = ownerCode == null;

            // Surface the legacy single "CATALOGUE" header as "Default" so it shows up nicely.
            if (string.Equals(headerCode, headerCodePrefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(name, headerCode, StringComparison.OrdinalIgnoreCase))
            {
                name = "Default";
            }

            list.Add(new SavedLayoutInfo
            {
                HeaderCode = headerCode,
                Name = name,
                IsPublic = isPublic,
                CreatedBy = createdBy,
                CanEdit = !isPublic
                    || string.Equals(createdBy, userCode, StringComparison.OrdinalIgnoreCase),
                LastModified = modified
            });
        }

        return list;
    }

    public async Task<string> SaveNamedLayoutAsync(
        string moduleCode, string headerCodePrefix, string headerDescription,
        string userCode, string layoutName, bool isPublic, Dictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
            throw new ArgumentException("Layout name is required.", nameof(layoutName));

        var slug = SlugifyLayoutName(layoutName);
        var headerCode = BuildNamedHeaderCode(headerCodePrefix, slug);
        var displayName = layoutName.Trim();
        if (displayName.Length > 100) displayName = displayName.Substring(0, 100);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            // Look for an existing header with same code (regardless of user) so we can detect ownership
            // conflicts on public layouts before we touch anything.
            const string lookupSql = @"
                SELECT TOP 1 pk_IniHeaderID, fk_UserCode, CreatedBy
                FROM tbl_IniHeader
                WHERE fk_IniModuleCode = @ModuleCode
                  AND IniHeaderCode = @HeaderCode
                  AND fk_StoreCode IS NULL
                  AND (
                       (@IsPublic = 1 AND fk_UserCode IS NULL)
                    OR (@IsPublic = 0 AND fk_UserCode = @UserCode)
                  )";

            long headerId = 0;
            string? existingCreatedBy = null;
            using (var cmd = new SqlCommand(lookupSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
                cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
                cmd.Parameters.AddWithValue("@UserCode", userCode);
                cmd.Parameters.AddWithValue("@IsPublic", isPublic ? 1 : 0);

                using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    headerId = rdr.GetInt64(0);
                    existingCreatedBy = rdr.IsDBNull(2) ? null : rdr.GetString(2);
                }
            }

            if (headerId > 0 && isPublic
                && !string.Equals(existingCreatedBy, userCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Public layout '{displayName}' is owned by '{existingCreatedBy}' and cannot be overwritten by '{userCode}'.");
            }

            if (headerId == 0)
            {
                // Create. Make sure the module row exists (legacy schema requires it).
                const string createSql = @"
                    IF NOT EXISTS (SELECT 1 FROM tbl_IniModule WHERE pk_IniModuleCode = @ModuleCode)
                        INSERT INTO tbl_IniModule (pk_IniModuleCode, IniModuleDesc)
                        VALUES (@ModuleCode, @HeaderDescr);

                    INSERT INTO tbl_IniHeader WITH (ROWLOCK)
                        (fk_IniModuleCode, IniHeaderCode, IniHeaderDescr, fk_UserCode, fk_StoreCode,
                         CreatedBy, LastModifiedBy, LastModifiedDateTime)
                    VALUES
                        (@ModuleCode, @HeaderCode, @DisplayName,
                         CASE WHEN @IsPublic = 1 THEN NULL ELSE @UserCode END, NULL,
                         @UserCode, @UserCode, GETUTCDATE());

                    SELECT CAST(SCOPE_IDENTITY() AS bigint);";

                using var cmd = new SqlCommand(createSql, conn, transaction);
                cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
                cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
                cmd.Parameters.AddWithValue("@DisplayName", displayName);
                cmd.Parameters.AddWithValue("@HeaderDescr", headerDescription);
                cmd.Parameters.AddWithValue("@UserCode", userCode);
                cmd.Parameters.AddWithValue("@IsPublic", isPublic ? 1 : 0);
                var idObj = await cmd.ExecuteScalarAsync();
                headerId = Convert.ToInt64(idObj);
            }
            else
            {
                // Refresh display name (user may have edited it) + bump LastModified.
                const string updSql = @"
                    UPDATE tbl_IniHeader WITH (ROWLOCK)
                    SET IniHeaderDescr = @DisplayName,
                        LastModifiedBy = @UserCode,
                        LastModifiedDateTime = GETUTCDATE()
                    WHERE pk_IniHeaderID = @HeaderID;
                    DELETE FROM tbl_IniDetail WITH (ROWLOCK) WHERE fk_IniHeaderID = @HeaderID;";

                using var cmd = new SqlCommand(updSql, conn, transaction);
                cmd.Parameters.AddWithValue("@DisplayName", displayName);
                cmd.Parameters.AddWithValue("@UserCode", userCode);
                cmd.Parameters.AddWithValue("@HeaderID", headerId);
                await cmd.ExecuteNonQueryAsync();
            }

            if (parameters.Count > 0)
            {
                using var insertCmd = new SqlCommand { Connection = conn, Transaction = transaction };
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
            }

            transaction.Commit();
            return headerCode;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<Dictionary<string, string>> GetNamedLayoutAsync(string moduleCode, string headerCode, string userCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Visibility: own private OR public (NULL user).
        const string sql = @"
            SELECT d.ParmCode, d.ParmValue
            FROM tbl_IniDetail d
            INNER JOIN tbl_IniHeader h ON d.fk_IniHeaderID = h.pk_IniHeaderID
            WHERE h.fk_IniModuleCode = @ModuleCode
              AND h.IniHeaderCode = @HeaderCode
              AND h.fk_StoreCode IS NULL
              AND (h.fk_UserCode IS NULL OR h.fk_UserCode = @UserCode)";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
        cmd.Parameters.AddWithValue("@HeaderCode", headerCode);
        cmd.Parameters.AddWithValue("@UserCode", userCode);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetString(1);

        return result;
    }

    public async Task<bool> DeleteNamedLayoutAsync(string moduleCode, string headerCode, string userCode)
    {
        // Authorization: private layouts deletable only by owner; public layouts only by CreatedBy.
        const string sql = @"
            DECLARE @HeaderID bigint = 0;
            DECLARE @Owner    nvarchar(100) = NULL;
            DECLARE @Creator  nvarchar(100) = NULL;

            SELECT TOP 1 @HeaderID = pk_IniHeaderID, @Owner = fk_UserCode, @Creator = CreatedBy
            FROM tbl_IniHeader
            WHERE fk_IniModuleCode = @ModuleCode
              AND IniHeaderCode = @HeaderCode
              AND fk_StoreCode IS NULL;

            IF @HeaderID = 0
            BEGIN
                SELECT 0 AS Deleted;
                RETURN;
            END

            -- Authorization check
            IF (@Owner IS NULL AND @Creator <> @UserCode)
               OR (@Owner IS NOT NULL AND @Owner <> @UserCode)
            BEGIN
                SELECT 0 AS Deleted; -- not authorized
                RETURN;
            END

            DELETE FROM tbl_IniDetail WHERE fk_IniHeaderID = @HeaderID;
            DELETE FROM tbl_IniHeader WHERE pk_IniHeaderID = @HeaderID;
            SELECT 1 AS Deleted;";

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
