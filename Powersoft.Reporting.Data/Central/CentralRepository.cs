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

    public async Task<List<(string UserCode, string DisplayName, string Email)>> GetUsersForDatabaseAsync(string dbCode)
    {
        const string sql = @"
            SELECT u.pk_UserCode, ISNULL(u.UserDesc, u.pk_UserCode) AS DisplayName, ISNULL(u.UserEmail,'') AS Email
            FROM tbl_User u
            INNER JOIN tbl_RelUserDB rud ON u.pk_UserCode = rud.fk_UserCode
            WHERE rud.fk_DBCode = @DBCode
              AND u.UserActive = 1
              AND u.UserEmail IS NOT NULL AND u.UserEmail <> ''
            ORDER BY u.UserDesc";

        var results = new List<(string, string, string)>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DBCode", dbCode);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0).Trim(),
                reader.GetString(1).Trim(),
                reader.GetString(2).Trim()
            ));
        }
        return results;
    }

    // ==================== Industry template packs ====================

    public async Task EnsureTemplatePackSchemaAsync()
    {
        const string sql = @"
            IF OBJECT_ID('dbo.tbl_RE_TemplatePack', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tbl_RE_TemplatePack (
                    pk_PackID     INT IDENTITY(1,1) CONSTRAINT PK_RE_TemplatePack PRIMARY KEY,
                    PackCode      NVARCHAR(50)  NOT NULL CONSTRAINT UQ_RE_TemplatePack_Code UNIQUE,
                    PackName      NVARCHAR(200) NOT NULL,
                    IndustryTag   NVARCHAR(100) NULL,
                    Description   NVARCHAR(500) NULL,
                    SortOrder     INT NOT NULL CONSTRAINT DF_RE_TP_Sort   DEFAULT(0),
                    IsActive      BIT NOT NULL CONSTRAINT DF_RE_TP_Active DEFAULT(1),
                    CreatedBy     NVARCHAR(100) NULL,
                    CreatedDate   DATETIME NOT NULL CONSTRAINT DF_RE_TP_Created DEFAULT(GETDATE()),
                    ModifiedBy    NVARCHAR(100) NULL,
                    ModifiedDate  DATETIME NULL
                );
            END
            IF OBJECT_ID('dbo.tbl_RE_TemplatePackItem', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tbl_RE_TemplatePackItem (
                    pk_ItemID         INT IDENTITY(1,1) CONSTRAINT PK_RE_TemplatePackItem PRIMARY KEY,
                    fk_PackID         INT NOT NULL CONSTRAINT FK_RE_TPItem_Pack
                                          REFERENCES dbo.tbl_RE_TemplatePack(pk_PackID) ON DELETE CASCADE,
                    ReportType        NVARCHAR(50)  NOT NULL,
                    TemplateName      NVARCHAR(200) NOT NULL,
                    ParametersJson    NVARCHAR(MAX) NULL,
                    RecurrenceType    NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_TPItem_Rec  DEFAULT('Monthly'),
                    RecurrenceDay     INT NULL,
                    ScheduleTimeMin   INT NOT NULL CONSTRAINT DF_RE_TPItem_Time DEFAULT(480),
                    ExportFormat      NVARCHAR(20)  NOT NULL CONSTRAINT DF_RE_TPItem_Fmt  DEFAULT('Excel'),
                    IncludeAiAnalysis BIT NOT NULL CONSTRAINT DF_RE_TPItem_Ai   DEFAULT(0),
                    AiLocale          NVARCHAR(10)  NOT NULL CONSTRAINT DF_RE_TPItem_Loc  DEFAULT('en'),
                    SkipIfEmpty       BIT NOT NULL CONSTRAINT DF_RE_TPItem_Skip DEFAULT(1),
                    SortOrder         INT NOT NULL CONSTRAINT DF_RE_TPItem_Sort DEFAULT(0)
                );
                CREATE INDEX IX_RE_TPItem_Pack ON dbo.tbl_RE_TemplatePackItem (fk_PackID);
            END";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ReportTemplatePack>> GetTemplatePacksAsync()
    {
        const string sql = @"
            SELECT pk_PackID, PackCode, PackName, IndustryTag, Description, SortOrder
            FROM dbo.tbl_RE_TemplatePack WHERE IsActive = 1 ORDER BY SortOrder, PackName;

            SELECT fk_PackID, pk_ItemID, ReportType, TemplateName, ParametersJson,
                   RecurrenceType, RecurrenceDay, ScheduleTimeMin, ExportFormat,
                   IncludeAiAnalysis, AiLocale, SkipIfEmpty
            FROM dbo.tbl_RE_TemplatePackItem
            ORDER BY fk_PackID, SortOrder, pk_ItemID;";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        var byId = new Dictionary<int, ReportTemplatePack>();
        var ordered = new List<ReportTemplatePack>();
        while (await reader.ReadAsync())
        {
            var pack = new ReportTemplatePack
            {
                PackCode = reader.GetString(1),
                PackName = reader.GetString(2),
                IndustryTag = reader.IsDBNull(3) ? null : reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.GetInt32(5)
            };
            byId[reader.GetInt32(0)] = pack;
            ordered.Add(pack);
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            if (byId.TryGetValue(reader.GetInt32(0), out var pack))
                pack.Items.Add(MapTemplateItem(reader));
        }

        return ordered;
    }

    public async Task<ReportTemplatePack?> GetTemplatePackAsync(string packCode)
    {
        const string sql = @"
            SELECT pk_PackID, PackCode, PackName, IndustryTag, Description, SortOrder
            FROM dbo.tbl_RE_TemplatePack WHERE PackCode = @Code;

            SELECT i.fk_PackID, i.pk_ItemID, i.ReportType, i.TemplateName, i.ParametersJson,
                   i.RecurrenceType, i.RecurrenceDay, i.ScheduleTimeMin, i.ExportFormat,
                   i.IncludeAiAnalysis, i.AiLocale, i.SkipIfEmpty
            FROM dbo.tbl_RE_TemplatePackItem i
            INNER JOIN dbo.tbl_RE_TemplatePack p ON i.fk_PackID = p.pk_PackID
            WHERE p.PackCode = @Code
            ORDER BY i.SortOrder, i.pk_ItemID;";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Code", packCode);
        using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;
        var pack = new ReportTemplatePack
        {
            PackCode = reader.GetString(1),
            PackName = reader.GetString(2),
            IndustryTag = reader.IsDBNull(3) ? null : reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            SortOrder = reader.GetInt32(5)
        };

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
            pack.Items.Add(MapTemplateItem(reader));

        return pack;
    }

    private static ReportTemplateItem MapTemplateItem(SqlDataReader r) => new()
    {
        // Column order matches both item SELECTs above.
        ItemKey = r.GetInt32(1).ToString(),   // pk_ItemID — stable idempotency key
        ReportType = r.GetString(2),
        TemplateName = r.GetString(3),
        ParametersJson = r.IsDBNull(4) ? null : r.GetString(4),
        RecurrenceType = r.GetString(5),
        RecurrenceDay = r.IsDBNull(6) ? null : r.GetInt32(6),
        ScheduleTime = TimeSpan.FromMinutes(r.GetInt32(7)),
        ExportFormat = r.GetString(8),
        IncludeAiAnalysis = r.GetBoolean(9),
        AiLocale = r.GetString(10),
        SkipIfEmpty = r.GetBoolean(11)
    };

    public async Task UpsertTemplatePackAsync(ReportTemplatePack pack, string userCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            // 1) Upsert header, resolve pk_PackID.
            int packId;
            using (var find = new SqlCommand(
                "SELECT pk_PackID FROM dbo.tbl_RE_TemplatePack WHERE PackCode = @Code", conn, tx))
            {
                find.Parameters.AddWithValue("@Code", pack.PackCode);
                var existing = await find.ExecuteScalarAsync();

                if (existing == null || existing == DBNull.Value)
                {
                    using var ins = new SqlCommand(@"
                        INSERT INTO dbo.tbl_RE_TemplatePack
                            (PackCode, PackName, IndustryTag, Description, SortOrder, IsActive, CreatedBy, CreatedDate)
                        VALUES (@Code, @Name, @Tag, @Desc, @Sort, 1, @User, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx);
                    AddPackHeaderParams(ins, pack, userCode);
                    packId = (int)(await ins.ExecuteScalarAsync())!;
                }
                else
                {
                    packId = (int)existing;
                    using var upd = new SqlCommand(@"
                        UPDATE dbo.tbl_RE_TemplatePack
                        SET PackName = @Name, IndustryTag = @Tag, Description = @Desc,
                            SortOrder = @Sort, ModifiedBy = @User, ModifiedDate = GETDATE()
                        WHERE pk_PackID = @Id;", conn, tx);
                    AddPackHeaderParams(upd, pack, userCode);
                    upd.Parameters.AddWithValue("@Id", packId);
                    await upd.ExecuteNonQueryAsync();
                }
            }

            // 2) Replace the full item list (simplest correct sync of an edited pack).
            using (var del = new SqlCommand(
                "DELETE FROM dbo.tbl_RE_TemplatePackItem WHERE fk_PackID = @Id", conn, tx))
            {
                del.Parameters.AddWithValue("@Id", packId);
                await del.ExecuteNonQueryAsync();
            }

            var sort = 0;
            foreach (var item in pack.Items)
            {
                using var ins = new SqlCommand(@"
                    INSERT INTO dbo.tbl_RE_TemplatePackItem
                        (fk_PackID, ReportType, TemplateName, ParametersJson, RecurrenceType, RecurrenceDay,
                         ScheduleTimeMin, ExportFormat, IncludeAiAnalysis, AiLocale, SkipIfEmpty, SortOrder)
                    VALUES (@PackId, @Type, @Name, @Params, @Rec, @Day, @Time, @Fmt, @Ai, @Loc, @Skip, @Sort);", conn, tx);
                ins.Parameters.AddWithValue("@PackId", packId);
                ins.Parameters.AddWithValue("@Type", item.ReportType);
                ins.Parameters.AddWithValue("@Name", item.TemplateName);
                ins.Parameters.AddWithValue("@Params", (object?)item.ParametersJson ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Rec", string.IsNullOrWhiteSpace(item.RecurrenceType) ? "Monthly" : item.RecurrenceType);
                ins.Parameters.AddWithValue("@Day", (object?)item.RecurrenceDay ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Time", (int)item.ScheduleTime.TotalMinutes);
                ins.Parameters.AddWithValue("@Fmt", string.IsNullOrWhiteSpace(item.ExportFormat) ? "Excel" : item.ExportFormat);
                ins.Parameters.AddWithValue("@Ai", item.IncludeAiAnalysis);
                ins.Parameters.AddWithValue("@Loc", string.IsNullOrWhiteSpace(item.AiLocale) ? "en" : item.AiLocale);
                ins.Parameters.AddWithValue("@Skip", item.SkipIfEmpty);
                ins.Parameters.AddWithValue("@Sort", sort++);
                await ins.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static void AddPackHeaderParams(SqlCommand cmd, ReportTemplatePack pack, string userCode)
    {
        cmd.Parameters.AddWithValue("@Code", pack.PackCode);
        cmd.Parameters.AddWithValue("@Name", pack.PackName);
        cmd.Parameters.AddWithValue("@Tag", (object?)pack.IndustryTag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object?)pack.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sort", pack.SortOrder);
        cmd.Parameters.AddWithValue("@User", (object?)userCode ?? DBNull.Value);
    }

    public async Task DeleteTemplatePackAsync(string packCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.tbl_RE_TemplatePack WHERE PackCode = @Code", conn);
        cmd.Parameters.AddWithValue("@Code", packCode);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SeedTemplatePacksIfEmptyAsync(IEnumerable<ReportTemplatePack> seedPacks, string userCode)
    {
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.tbl_RE_TemplatePack", conn);
            var n = (int)(await count.ExecuteScalarAsync())!;
            if (n > 0) return;
        }

        foreach (var pack in seedPacks)
            await UpsertTemplatePackAsync(pack, userCode);
    }
}
