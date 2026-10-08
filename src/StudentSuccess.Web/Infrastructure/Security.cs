using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace StudentSuccess.Web.Infrastructure;

/// <summary>The staff account, configured with Auth__Username and Auth__Password (never in source code).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "admin";

    /// <summary>Empty = nobody can sign in (secure default).</summary>
    public string? Password { get; set; }
}

public interface ICredentialChecker
{
    bool IsConfigured { get; }

    bool IsValid(string? username, string? password);
}

public sealed class CredentialChecker(IOptions<AuthOptions> options) : ICredentialChecker
{
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.Password);

    public bool IsValid(string? username, string? password)
    {
        if (!IsConfigured || username is null || password is null)
        {
            return false;
        }

        // Compare both values in constant time so response timing doesn't reveal
        // how much of a guess was right. '&' (not '&&') so both are always checked.
        return FixedTimeEquals(username.Trim(), options.Value.Username)
             & FixedTimeEquals(password, options.Value.Password!);
    }

    internal static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

public static class UserExtensions
{
    /// <summary>Name recorded in the audit log.</summary>
    public static string ChangedBy(this ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name ? name[..Math.Min(name.Length, 100)] : "unknown";
}

public static class RateLimitPolicies
{
    public const string Login = "login";

    /// <summary>Each client IP gets 5 sign-in attempts per minute, which slows password guessing.</summary>
    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(Login, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }
}

/// <summary>Standard security headers on every response.</summary>
public static class SecurityHeaders
{
    // Only this site's own scripts may run (no inline scripts); docs.html also loads Swagger UI from jsDelivr
    private const string Policy =
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "style-src 'self' https://cdn.jsdelivr.net 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Content-Security-Policy"] = Policy;
            await next();
        });
}
