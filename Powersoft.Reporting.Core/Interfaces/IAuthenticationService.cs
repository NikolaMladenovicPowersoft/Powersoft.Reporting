using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IAuthenticationService
{
    Task<LoginResult> AuthenticateAsync(LoginRequest request);
    Task<AppUser?> GetUserByUsernameAsync(string username);
    Task<bool> UserExistsAsync(string username);
}
