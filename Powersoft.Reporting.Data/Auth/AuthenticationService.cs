using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Auth;

public class AuthenticationService : IAuthenticationService
{
    private readonly string _centralConnectionString;

    public AuthenticationService(string centralConnectionString)
    {
        _centralConnectionString = centralConnectionString;
    }

    public async Task<LoginResult> AuthenticateAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return LoginResult.Failed("Username and password are required.");
        }

        try
        {
            var user = await ValidateUserAsync(request.Username, request.Password);
            
            if (user != null)
            {
                return LoginResult.Succeeded(user);
            }
            
            return LoginResult.Failed("Invalid username or password.");
        }
        catch (Exception ex)
        {
            return LoginResult.Failed($"Authentication error: {ex.Message}");
        }
    }

    private async Task<AppUser?> ValidateUserAsync(string username, string password)
    {
        const string sql = @"
            SELECT u.pk_UserCode, u.UserDesc, u.UserPassword, u.UserActive,
                   u.fk_RoleID, r.RoleName, r.Ranking, u.fk_CompanyCode
            FROM tbl_User u
            INNER JOIN tbl_Role r ON u.fk_RoleID = r.pk_RoleID
            WHERE u.pk_UserCode = @Username
              AND u.UserActive = 1
              AND r.RoleActive = 1";

        using var conn = new SqlConnection(_centralConnectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var storedPasswordHash = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var userDesc = reader.IsDBNull(1) ? username : reader.GetString(1);

            var inputHashUtf8 = GenerateSha1HashUtf8(password);
            var inputHashUnicode = GenerateSha1HashUnicode(password);
            var trimmedStored = (storedPasswordHash ?? "").Trim();
            
            if (string.Equals(trimmedStored, inputHashUtf8, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedStored, inputHashUnicode, StringComparison.OrdinalIgnoreCase))
            {
                return new AppUser
                {
                    Username = username,
                    DisplayName = userDesc,
                    IsActive = true,
                    RoleID = reader.GetInt32(4),
                    RoleName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Ranking = reader.IsDBNull(6) ? 99 : reader.GetInt32(6),
                    CompanyCode = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
            }
        }

        return null;
    }

    private static string GenerateSha1HashUtf8(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string GenerateSha1HashUnicode(string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task<AppUser?> GetUserByUsernameAsync(string username)
    {
        const string sql = @"
            SELECT u.pk_UserCode, u.UserDesc, u.UserActive,
                   u.fk_RoleID, r.RoleName, r.Ranking, u.fk_CompanyCode
            FROM tbl_User u
            LEFT JOIN tbl_Role r ON u.fk_RoleID = r.pk_RoleID
            WHERE u.pk_UserCode = @Username";

        using var conn = new SqlConnection(_centralConnectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new AppUser
            {
                Username = reader.GetString(0),
                DisplayName = reader.IsDBNull(1) ? username : reader.GetString(1),
                IsActive = !reader.IsDBNull(2) && reader.GetBoolean(2),
                RoleID = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                RoleName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Ranking = reader.IsDBNull(5) ? 99 : reader.GetInt32(5),
                CompanyCode = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }

        return null;
    }
    
    public async Task<bool> UserExistsAsync(string username)
    {
        const string sql = "SELECT COUNT(*) FROM tbl_User WHERE pk_UserCode = @Username";

        using var conn = new SqlConnection(_centralConnectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        await conn.OpenAsync();
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        
        return count > 0;
    }
}
