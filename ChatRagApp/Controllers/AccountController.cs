using ChatRagApp.Models;
using ChatRagApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChatRagApp.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly IQdrantService _qdrant;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IQdrantService qdrant, ILogger<AccountController> logger)
    {
        _qdrant = qdrant;
        _logger = logger;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl ?? "/";
        return View();
    }

    [HttpGet("external-login")]
    [AllowAnonymous]
    public IActionResult ExternalLogin(string provider = "Google", string returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

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
                return Redirect("/account/login?error=auth_failed");
            }

            var principal = authResult.Principal;
            var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var nameId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? email;

            if (string.IsNullOrEmpty(email))
                return Redirect("/account/login?error=no_email");

            // Upsert user in Qdrant
            var existingUser = await _qdrant.GetUserByEmailAsync(email);
            var user = new AppUser
            {
                Id = existingUser?.Id ?? Guid.NewGuid(),
                Email = email,
                DisplayName = name,
                Provider = "google",
                LastLogin = DateTime.UtcNow,
                IsActive = true
            };
            await _qdrant.UpsertUserAsync(user);

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
        return Redirect("/account/login");
    }
}
