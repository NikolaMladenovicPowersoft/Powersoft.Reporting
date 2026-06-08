using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Central;

public class CentralRepository : ICentralRepository
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
                   Address1, Address2, Tel1, Email
            FROM tbl_Company 
            WHERE CompanyActive = 1 
            ORDER BY CompanyName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            companies.Add(MapCompany(reader));
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
            databases.Add(MapDatabase(reader));
        }
        
        return databases;
    }

    public async Task<List<Database>> GetDatabasesLinkedToModuleAsync()
    {
        var databases = new List<Database>();

        const string sql = @"
            SELECT DISTINCT d.pk_DBCode, d.DBFriendlyName, d.DBName,
                   d.DBServerID, d.DBProviderInstanceName,
                   d.DBUserName, d.DBPassword, d.DBActive,
                   c.pk_CompanyCode, c.CompanyName
            FROM tbl_DB d
            INNER JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
            INNER JOIN tbl_RelModuleDb rmd ON d.pk_DBCode = rmd.fk_DbCode
            WHERE d.DBActive = 1
              AND c.CompanyActive = 1
              AND rmd.fk_ModuleCode = @ModuleCode
            ORDER BY c.CompanyName, d.DBFriendlyName";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ModuleCode", ModuleConstants.ModuleCode);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            databases.Add(MapDatabase(reader));

        return databases;
    }

    /// <summary>
    /// Returns all databases the user can access, respecting ranking, module linkage, and user-DB mapping.
    /// This is the core login query per Christina's spec (MASTER_CONTEXT Section 5.3).
    /// </summary>
    public async Task<List<Database>> GetAccessibleDatabasesAsync(string userCode, int ranking)
    {
        var databases = new List<Database>();

        // System admin (Ranking < 15): see ALL active companies/databases — no module/user filter
        if (ranking < ModuleConstants.RankingSystemAdmin)
        {
            const string sql = @"
                SELECT d.pk_DBCode, d.DBFriendlyName, d.DBName, 
                       d.DBServerID, d.DBProviderInstanceName,
                       d.DBUserName, d.DBPassword, d.DBActive,
                       c.pk_CompanyCode, c.CompanyName
                FROM tbl_DB d
                INNER JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
                WHERE d.DBActive = 1 AND c.CompanyActive = 1
                ORDER BY c.CompanyName, d.DBFriendlyName";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                databases.Add(MapDatabase(reader));
            }
        }
        else
        {
            // Client users (Ranking >= 15): filter by user-DB link + module linkage
            const string sql = @"
                SELECT DISTINCT d.pk_DBCode, d.DBFriendlyName, d.DBName,
                       d.DBServerID, d.DBProviderInstanceName,
                       d.DBUserName, d.DBPassword, d.DBActive,
                       c.pk_CompanyCode, c.CompanyName
                FROM tbl_User u
                INNER JOIN tbl_Role r ON u.fk_RoleID = r.pk_RoleID
                INNER JOIN tbl_RelUserDB rud ON u.pk_UserCode = rud.fk_UserCode
                INNER JOIN tbl_DB d ON rud.fk_DBCode = d.pk_DBCode
                INNER JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
                INNER JOIN tbl_RelModuleDb rmd ON d.pk_DBCode = rmd.fk_DbCode
                WHERE u.pk_UserCode = @UserCode
                  AND u.UserActive = 1
                  AND d.DBActive = 1
                  AND c.CompanyActive = 1
                  AND rmd.fk_ModuleCode = @ModuleCode
                ORDER BY c.CompanyName, d.DBFriendlyName";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserCode", userCode);
            cmd.Parameters.AddWithValue("@ModuleCode", ModuleConstants.ModuleCode);
            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                databases.Add(MapDatabase(reader));
            }
        }

        return databases;
    }

    /// <summary>
    /// Verifies that a user can access a specific database.
    /// System admins (Ranking &lt; 15) can access any active database.
    /// Client users must be linked to the DB and the DB must be linked to RENGINEAI.
    /// </summary>
    public async Task<bool> CanUserAccessDatabaseAsync(string userCode, int ranking, string dbCode)
    {
        if (ranking < ModuleConstants.RankingSystemAdmin)
        {
            // System admin — just check DB is active and company is active
            const string sql = @"
                SELECT COUNT(*)
                FROM tbl_DB d
                INNER JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
                WHERE d.pk_DBCode = @DBCode AND d.DBActive = 1 AND c.CompanyActive = 1";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@DBCode", dbCode);
            await conn.OpenAsync();
            return (int)(await cmd.ExecuteScalarAsync() ?? 0) > 0;
        }
        else
        {
            // Client user — check full chain: user-DB link + module link + active checks
            const string sql = @"
                SELECT COUNT(*)
                FROM tbl_RelUserDB rud
                INNER JOIN tbl_DB d ON rud.fk_DBCode = d.pk_DBCode
                INNER JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
                INNER JOIN tbl_RelModuleDb rmd ON d.pk_DBCode = rmd.fk_DbCode
                WHERE rud.fk_UserCode = @UserCode
                  AND rud.fk_DBCode = @DBCode
                  AND d.DBActive = 1
                  AND c.CompanyActive = 1
                  AND rmd.fk_ModuleCode = @ModuleCode";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserCode", userCode);
            cmd.Parameters.AddWithValue("@DBCode", dbCode);
            cmd.Parameters.AddWithValue("@ModuleCode", ModuleConstants.ModuleCode);
            await conn.OpenAsync();
            return (int)(await cmd.ExecuteScalarAsync() ?? 0) > 0;
        }
    }

    /// <summary>
    /// Checks tbl_RelRoleAction for a specific role+action pair.
    /// Only needed for Ranking > 20 (custom roles). Ranking &lt;= 20 automatically has all actions.
    /// </summary>
    public async Task<bool> IsActionAuthorizedAsync(int roleId, int actionId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM tbl_RelRoleAction
            WHERE fk_RoleID = @RoleID AND fk_ActionID = @ActionID";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RoleID", roleId);
        cmd.Parameters.AddWithValue("@ActionID", actionId);
        await conn.OpenAsync();
        return (int)(await cmd.ExecuteScalarAsync() ?? 0) > 0;
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
            return MapDatabase(reader);
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

    private static Company MapCompany(SqlDataReader reader)
    {
        return new Company
        {
            CompanyCode = reader.GetString(0),
            CompanyName = reader.GetString(1),
            CompanyActive = reader.GetBoolean(2),
            Address1 = reader.IsDBNull(3) ? null : reader.GetString(3),
            Address2 = reader.IsDBNull(4) ? null : reader.GetString(4),
            Tel1 = reader.IsDBNull(5) ? null : reader.GetString(5),
            Email = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }

    public async Task<Dictionary<string, string>> GetSystemSettingsAsync(string parameterPrefix)
    {
        const string sql = @"
            SELECT pk_ParameterCode, ParameterValue
            FROM tbl_SystemSettings
            WHERE pk_ParameterCode LIKE @Prefix + '%'";

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Prefix", parameterPrefix);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            settings[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        }

        return settings;
    }

    public async Task UpsertSystemSettingAsync(string parameterCode, string description, string dataType, string value)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM tbl_SystemSettings WHERE pk_ParameterCode = @Code)
                UPDATE tbl_SystemSettings
                SET ParameterValue = @Value,
                    ParameterDescription = @Desc,
                    ParameterDataType = @DataType
                WHERE pk_ParameterCode = @Code
            ELSE
                INSERT INTO tbl_SystemSettings (pk_ParameterCode, ParameterDescription, ParameterDataType, ParameterValue)
                VALUES (@Code, @Desc, @DataType, @Value)";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Code", parameterCode);
        cmd.Parameters.AddWithValue("@Desc", description);
        cmd.Parameters.AddWithValue("@DataType", dataType);
        cmd.Parameters.AddWithValue("@Value", value);

        await cmd.ExecuteNonQueryAsync();
    }

    // ==================== AI usage tracking (cross-tenant) ====================

    public async Task EnsureAiUsageLogSchemaAsync()
    {
        const string sql = @"
            IF OBJECT_ID('dbo.tbl_RE_AiUsageLog', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tbl_RE_AiUsageLog (
                    pk_LogID      BIGINT IDENTITY(1,1) CONSTRAINT PK_RE_AiUsageLog PRIMARY KEY,
                    DBCode        NVARCHAR(50)  NOT NULL,
                    DBName        NVARCHAR(200) NULL,
                    UserCode      NVARCHAR(100) NULL,
                    ReportType    NVARCHAR(50)  NULL,
                    InputTokens   INT           NOT NULL CONSTRAINT DF_RE_AiUsageLog_In  DEFAULT(0),
                    OutputTokens  INT           NOT NULL CONSTRAINT DF_RE_AiUsageLog_Out DEFAULT(0),
                    EstimatedCost DECIMAL(18,6) NOT NULL CONSTRAINT DF_RE_AiUsageLog_Est DEFAULT(0),
                    ActualCost    DECIMAL(18,6) NOT NULL CONSTRAINT DF_RE_AiUsageLog_Act DEFAULT(0),
                    Source        NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_AiUsageLog_Src DEFAULT('Interactive'),
                    AnalysisDate  DATETIME      NOT NULL CONSTRAINT DF_RE_AiUsageLog_Dt  DEFAULT(GETDATE())
                );
                CREATE INDEX IX_RE_AiUsageLog_Date   ON dbo.tbl_RE_AiUsageLog (AnalysisDate);
                CREATE INDEX IX_RE_AiUsageLog_DBCode ON dbo.tbl_RE_AiUsageLog (DBCode);
            END";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task LogAiUsageAsync(AiUsageLogEntry entry)
    {
        const string sql = @"
            INSERT INTO dbo.tbl_RE_AiUsageLog
                (DBCode, DBName, UserCode, ReportType, InputTokens, OutputTokens, EstimatedCost, ActualCost, Source, AnalysisDate)
            VALUES
                (@DBCode, @DBName, @UserCode, @ReportType, @InTok, @OutTok, @Est, @Act, @Source, GETDATE());";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DBCode", (object?)entry.DBCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DBName", (object?)entry.DBName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserCode", (object?)entry.UserCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ReportType", (object?)entry.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InTok", entry.InputTokens);
        cmd.Parameters.AddWithValue("@OutTok", entry.OutputTokens);
        cmd.Parameters.AddWithValue("@Est", entry.EstimatedCost);
        cmd.Parameters.AddWithValue("@Act", entry.ActualCost);
        cmd.Parameters.AddWithValue("@Source", string.IsNullOrEmpty(entry.Source) ? "Interactive" : entry.Source);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<AiUsageReport> GetAiUsageReportAsync(DateTime dateFrom, DateTime dateTo)
    {
        // Exclusive upper bound so the whole DateTo day is included regardless of time component.
        var fromDay = dateFrom.Date;
        var toExclusive = dateTo.Date.AddDays(1);

        var report = new AiUsageReport { DateFrom = fromDay, DateTo = dateTo.Date };

        const string sql = @"
            -- 1) Totals
            SELECT COUNT(*) AS Cnt,
                   ISNULL(SUM(CAST(InputTokens AS BIGINT) + OutputTokens), 0) AS Tok,
                   ISNULL(SUM(ActualCost), 0) AS Cost
            FROM dbo.tbl_RE_AiUsageLog
            WHERE AnalysisDate >= @From AND AnalysisDate < @ToExcl;

            -- 2) By company (LEFT JOIN to resolve the customer name from the DB code)
            SELECT COALESCE(c.CompanyName, log.DBName, log.DBCode) AS Label,
                   COUNT(*) AS Cnt,
                   ISNULL(SUM(CAST(log.InputTokens AS BIGINT) + log.OutputTokens), 0) AS Tok,
                   ISNULL(SUM(log.ActualCost), 0) AS Cost
            FROM dbo.tbl_RE_AiUsageLog log
            LEFT JOIN tbl_DB d      ON log.DBCode = d.pk_DBCode
            LEFT JOIN tbl_Company c ON d.fk_CompanyCode = c.pk_CompanyCode
            WHERE log.AnalysisDate >= @From AND log.AnalysisDate < @ToExcl
            GROUP BY COALESCE(c.CompanyName, log.DBName, log.DBCode)
            ORDER BY Cost DESC;

            -- 3) By report type
            SELECT ISNULL(ReportType, '(unknown)') AS Label,
                   COUNT(*) AS Cnt,
                   ISNULL(SUM(CAST(InputTokens AS BIGINT) + OutputTokens), 0) AS Tok,
                   ISNULL(SUM(ActualCost), 0) AS Cost
            FROM dbo.tbl_RE_AiUsageLog
            WHERE AnalysisDate >= @From AND AnalysisDate < @ToExcl
            GROUP BY ISNULL(ReportType, '(unknown)')
            ORDER BY Cost DESC;

            -- 4) By user
            SELECT ISNULL(NULLIF(UserCode, ''), '(unknown)') AS Label,
                   COUNT(*) AS Cnt,
                   ISNULL(SUM(CAST(InputTokens AS BIGINT) + OutputTokens), 0) AS Tok,
                   ISNULL(SUM(ActualCost), 0) AS Cost
            FROM dbo.tbl_RE_AiUsageLog
            WHERE AnalysisDate >= @From AND AnalysisDate < @ToExcl
            GROUP BY ISNULL(NULLIF(UserCode, ''), '(unknown)')
            ORDER BY Cost DESC;";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@From", fromDay);
        cmd.Parameters.AddWithValue("@ToExcl", toExclusive);

        using var reader = await cmd.ExecuteReaderAsync();

        // Result set 1: totals
        if (await reader.ReadAsync())
        {
            report.TotalAnalyses = reader.GetInt32(0);
            report.TotalTokens = reader.GetInt64(1);
            report.TotalCost = reader.GetDecimal(2);
        }

        await reader.NextResultAsync();
        report.ByCompany = await ReadGroupRowsAsync(reader);

        await reader.NextResultAsync();
        report.ByReport = await ReadGroupRowsAsync(reader);

        await reader.NextResultAsync();
        report.ByUser = await ReadGroupRowsAsync(reader);

        return report;
    }

    private static async Task<List<AiUsageGroupRow>> ReadGroupRowsAsync(SqlDataReader reader)
    {
        var rows = new List<AiUsageGroupRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new AiUsageGroupRow
            {
                Label = reader.IsDBNull(0) ? "(unknown)" : reader.GetString(0),
                AnalysisCount = reader.GetInt32(1),
                TotalTokens = reader.GetInt64(2),
                TotalCost = reader.GetDecimal(3)
            });
        }
        return rows;
    }

    private static Database MapDatabase(SqlDataReader reader)
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
}
