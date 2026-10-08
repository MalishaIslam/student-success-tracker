using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration: fail fast with a clear message
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("StudentSuccess");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'StudentSuccess' is not set. Run the app with `docker compose up` " +
        "(it is set from .env), or see README.md for running it with `dotnet run`.");
}

// ---------------------------------------------------------------------------
// Services (dependency injection)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<IStudentRepository, SqlStudentRepository>();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<ICredentialChecker, CredentialChecker>();

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddControllersWithViews(options =>
{
    // Every POST/PUT/DELETE from a form must carry a valid anti-forgery token (blocks CSRF)
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Sign-in with a cookie; every page needs a signed-in user unless marked [AllowAnonymous]
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "StudentSuccess.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            // API callers get 401 instead of being redirected to an HTML login page
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(RateLimitPolicies.Configure);
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline (order matters)
// ---------------------------------------------------------------------------
app.UseExceptionHandler("/home/error");                 // no stack traces or SQL text shown to users
app.UseStatusCodePagesWithReExecute("/home/status/{0}"); // friendly 404 / 403 pages
app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();   // /openapi/v1.json, shown by /docs.html
}

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// Lets tests reference the app's entry point
public partial class Program;
