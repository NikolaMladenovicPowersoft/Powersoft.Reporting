namespace Powersoft.Reporting.Core.Models;

public class AppUser
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
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
