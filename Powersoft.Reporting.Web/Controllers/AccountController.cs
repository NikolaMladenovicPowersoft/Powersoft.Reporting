using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.ViewModels;
using IAppAuthService = Powersoft.Reporting.Core.Interfaces.IAuthenticationService;

namespace Powersoft.Reporting.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAppAuthService _authService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAppAuthService authService, ILogger<AccountController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result = await _authService.AuthenticateAsync(new LoginRequest
            {
                Username = model.Username,
                Password = model.Password,
                RememberMe = model.RememberMe
            });

            if (result.Success && result.User != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, result.User.Username),
                    new Claim(ClaimTypes.GivenName, result.User.DisplayName),
                    new Claim(ClaimTypes.Role, result.User.Role)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe 
                        ? DateTimeOffset.UtcNow.AddDays(30) 
                        : DateTimeOffset.UtcNow.AddHours(8)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                _logger.LogInformation("User {Username} logged in successfully", model.Username);

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            model.ErrorMessage = "Invalid username or password.";
            _logger.LogWarning("Failed login attempt for user {Username}", model.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {Username}", model.Username);
            model.ErrorMessage = "An error occurred during login. Please try again.";
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        
        _logger.LogInformation("User {Username} logged out", username);
        
        return RedirectToAction("Login");
    }

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
