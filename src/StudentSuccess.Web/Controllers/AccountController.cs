using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudentSuccess.Web.Infrastructure;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Controllers;

[Route("account")]
public sealed class AccountController(ICredentialChecker credentials, ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        ViewData["NotConfigured"] = !credentials.IsConfigured;
        return View(new LoginForm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginForm form)
    {
        ViewData["NotConfigured"] = !credentials.IsConfigured;
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        if (!credentials.IsValid(form.Username, form.Password))
        {
            logger.LogWarning("Failed sign-in for user {User}", form.Username);
            // Same message whether the username or the password was wrong
            ModelState.AddModelError(string.Empty, "Username or password is incorrect.");
            form.Password = null;
            return View(form);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, form.Username!.Trim())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Only redirect back to pages on this site (prevents open-redirect attacks)
        return Url.IsLocalUrl(form.ReturnUrl) ? LocalRedirect(form.ReturnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
