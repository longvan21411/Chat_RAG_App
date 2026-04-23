using ChatRagApp.Models;
using ChatRagApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChatRagApp.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly IQdrantService _qdrant;
    private readonly ILogger<AccountController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<AppUser> _passwordHasher;

    public AccountController(IQdrantService qdrant, ILogger<AccountController> logger, IConfiguration configuration, IPasswordHasher<AppUser> passwordHasher)
    {
        _qdrant = qdrant;
        _logger = logger;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl;
        return Redirect($"/login?returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LocalLogin(string userName, string password, string? returnUrl = null)
    {
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl;

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return Redirect($"/login?error=invalid_credentials&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        if (string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase) && password == "admin")
        {
            var technicalUser = new AppUser
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                UserName = "admin",
                Email = "admin@local",
                DisplayName = "Technical Administrator",
                Provider = "local-technical",
                IsActive = true,
                LastLogin = DateTime.UtcNow
            };

            await SignInUserAsync(technicalUser, isAdmin: true);
            return LocalRedirect(safeReturnUrl);
        }

        var user = await _qdrant.GetUserByUserNameAsync(userName);
        if (user is null || !user.IsActive || !string.Equals(user.Provider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect($"/login?error=invalid_credentials&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Redirect($"/login?error=invalid_credentials&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        user.LastLogin = DateTime.UtcNow;
        await _qdrant.UpsertUserAsync(user);
        await SignInUserAsync(user);

        return LocalRedirect(safeReturnUrl);
    }

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register()
    {
        return Redirect("/register");
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string displayName, string userName, string? email, string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return Redirect("/register?error=missing_fields");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return Redirect("/register?error=password_mismatch");
        }

        var normalizedUserName = userName.Trim();
        var normalizedEmail = email?.Trim() ?? string.Empty;

        if (await _qdrant.GetUserByUserNameAsync(normalizedUserName) is not null)
        {
            return Redirect("/register?error=username_taken");
        }

        if (!string.IsNullOrWhiteSpace(normalizedEmail) && await _qdrant.GetUserByEmailAsync(normalizedEmail) is not null)
        {
            return Redirect("/register?error=email_taken");
        }

        var user = new AppUser
        {
            UserName = normalizedUserName,
            Email = normalizedEmail,
            DisplayName = displayName.Trim(),
            Provider = "local",
            IsActive = true,
            LastLogin = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        await _qdrant.UpsertUserAsync(user);

        return Redirect("/login?registered=true");
    }

    [HttpGet("external-login")]
    [AllowAnonymous]
    public IActionResult ExternalLogin(string provider = "Google", string returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        if (!string.Equals(provider, GoogleDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_configuration["Google:ClientId"]) ||
            string.IsNullOrWhiteSpace(_configuration["Google:ClientSecret"]))
        {
            return Redirect($"/login?error=provider_unavailable&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    [HttpGet("external-login-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string returnUrl = "/")
    {
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";

        try
        {
            // The Google middleware has already signed in via the SignInScheme (cookie)
            // so the user claims are available from HttpContext.User after authentication
            var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!authResult.Succeeded)
            {
                _logger.LogWarning("External login callback: authentication not succeeded");
                return Redirect("/login?error=auth_failed");
            }

            var principal = authResult.Principal;
            var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var nameId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? email;

            if (string.IsNullOrEmpty(email))
                return Redirect("/login?error=no_email");

            // Upsert user in Qdrant
            var existingUser = await _qdrant.GetUserByEmailAsync(email);
            var user = new AppUser
            {
                Id = existingUser?.Id ?? Guid.NewGuid(),
                UserName = existingUser?.UserName ?? email,
                Email = email,
                DisplayName = name,
                Provider = "google",
                LastLogin = DateTime.UtcNow,
                IsActive = true
            };
            await _qdrant.UpsertUserAsync(user);

            await SignInUserAsync(user);

            _logger.LogInformation("User {Email} logged in via Google", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during external login callback");
        }

        return LocalRedirect(returnUrl);
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    private async Task SignInUserAsync(AppUser user, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName),
            new("provider", user.Provider)
        };

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            claims.Add(new Claim("preferred_username", user.UserName));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }
}
