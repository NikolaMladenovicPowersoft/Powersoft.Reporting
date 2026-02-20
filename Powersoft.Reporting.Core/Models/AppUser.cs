namespace Powersoft.Reporting.Core.Models;

public class AppUser
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Role info from tbl_User JOIN tbl_Role
    public int RoleID { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int Ranking { get; set; } = 99; // Default to most restrictive

    // Company info from tbl_User
    public string? CompanyCode { get; set; }

    // Computed helpers
    public bool IsSystemAdmin => Ranking < 15;
    public bool IsClientAdmin => Ranking == 15;
    public bool IsClientStandard => Ranking == 20;
    public bool IsCustomRole => Ranking > 20;

    /// <summary>
    /// Ranking less than 15 = system/support users who see all companies and databases.
    /// Ranking 15+ = client users who are filtered by tbl_RelUserDB and tbl_RelModuleDb.
    /// </summary>
    public bool RequiresDbFiltering => Ranking >= 15;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public AppUser? User { get; set; }
    
    public static LoginResult Failed(string message) => new() { Success = false, ErrorMessage = message };
    public static LoginResult Succeeded(AppUser user) => new() { Success = true, User = user };
}
