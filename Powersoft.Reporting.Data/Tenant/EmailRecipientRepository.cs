using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

public class EmailRecipientRepository : IEmailRecipientRepository
{
    private const string Schema = "dboReportsAI";
    private readonly string _connectionString;

    public EmailRecipientRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<EmailRecipient>> GetAllAsync()
    {
        const string sql =
            "SELECT pk_RecipientID, EmailAddress, ISNULL(DisplayName,'') AS DisplayName," +
            " IsActive, CreatedDate, ISNULL(CreatedBy,'') AS CreatedBy" +
            " FROM dboReportsAI.tbl_EmailRecipientList" +
            " WHERE IsActive = 1" +
            " ORDER BY DisplayName, EmailAddress";

        var result = new List<EmailRecipient>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    public async Task<List<EmailRecipient>> SearchAsync(string query)
    {
        const string sql =
            "SELECT TOP 20 pk_RecipientID, EmailAddress, ISNULL(DisplayName,'') AS DisplayName," +
            " IsActive, CreatedDate, ISNULL(CreatedBy,'') AS CreatedBy" +
            " FROM dboReportsAI.tbl_EmailRecipientList" +
            " WHERE IsActive = 1" +
            "   AND (EmailAddress LIKE @q OR DisplayName LIKE @q)" +
            " ORDER BY DisplayName, EmailAddress";

        var result = new List<EmailRecipient>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    public async Task<EmailRecipient?> AddAsync(string emailAddress, string displayName, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return null;

        emailAddress = emailAddress.Trim().ToLowerInvariant();
        displayName  = (displayName ?? "").Trim();

        const string sql =
            "IF NOT EXISTS (" +
            "    SELECT 1 FROM dboReportsAI.tbl_EmailRecipientList" +
            "    WHERE EmailAddress = @email AND IsActive = 1" +
            ")" +
            "BEGIN" +
            "    INSERT INTO dboReportsAI.tbl_EmailRecipientList" +
            "        (EmailAddress, DisplayName, IsActive, CreatedDate, CreatedBy)" +
            "    VALUES (@email, @name, 1, GETDATE(), @createdBy);" +
            "END " +
            "SELECT pk_RecipientID, EmailAddress, ISNULL(DisplayName,'') AS DisplayName," +
            " IsActive, CreatedDate, ISNULL(CreatedBy,'') AS CreatedBy" +
            " FROM dboReportsAI.tbl_EmailRecipientList" +
            " WHERE EmailAddress = @email AND IsActive = 1;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@email", emailAddress);
        cmd.Parameters.AddWithValue("@name", string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName);
        cmd.Parameters.AddWithValue("@createdBy", string.IsNullOrEmpty(createdBy) ? (object)DBNull.Value : createdBy);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return Map(reader);
        return null;
    }

    public async Task<bool> DeleteAsync(int recipientId)
    {
        const string sql =
            "UPDATE dboReportsAI.tbl_EmailRecipientList" +
            " SET IsActive = 0" +
            " WHERE pk_RecipientID = @id;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", recipientId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static EmailRecipient Map(SqlDataReader r) => new()
    {
        RecipientId   = r.GetInt32(r.GetOrdinal("pk_RecipientID")),
        EmailAddress  = r.GetString(r.GetOrdinal("EmailAddress")),
        DisplayName   = r.GetString(r.GetOrdinal("DisplayName")),
        IsActive      = r.GetBoolean(r.GetOrdinal("IsActive")),
        CreatedDate   = r.GetDateTime(r.GetOrdinal("CreatedDate")),
        CreatedBy     = r.GetString(r.GetOrdinal("CreatedBy"))
    };
}
