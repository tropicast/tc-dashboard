using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>Public URL of the SPA, used in email links (<c>App</c> section).</summary>
internal sealed class AppOptions
{
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

/// <summary>Per-IP limits for the credential endpoints (<c>RateLimits:Auth</c>).</summary>
internal sealed class AuthRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Cookie security settings (<c>Auth</c> section).</summary>
internal sealed class AuthCookieOptions
{
    public bool UseSecureCookies { get; set; } = true;
    public string SessionCookieName => UseSecureCookies ? AuthSetup.SessionCookie : "tropicast";
    public string AntiforgeryCookieName => UseSecureCookies ? "__Host-tropicast-af" : "tropicast-af";
    public CookieSecurePolicy SecurePolicy => UseSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
}

internal static class AuthSetup
{
    /// <summary>The SPA's session cookie: HttpOnly, Secure, SameSite=Strict, host-only.</summary>
    internal const string SessionCookie = "__Host-tropicast";
    internal const string AntiforgeryHeader = "X-XSRF-TOKEN";
    /// <summary>Readable by the SPA, which echoes it in <see cref="AntiforgeryHeader"/>.</summary>
    internal const string AntiforgeryRequestCookie = "XSRF-TOKEN";
    internal const string AuthRateLimit = "auth";
    private const string SessionOrBearer = "SessionOrBearer";

    internal static IServiceCollection AddDashboardAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection("App"));
        services.Configure<AuthRateLimitOptions>(configuration.GetSection("RateLimits:Auth"));
        services.Configure<AuthCookieOptions>(configuration.GetSection("Auth"));
        var cookieOptions = configuration.GetSection("Auth").Get<AuthCookieOptions>() ?? new();

        // Cookies, bearer and refresh tokens are protected with these keys: keep them across container restarts.
        // DataProtection:KeysDirectory is a volume in production (deploy/compose.yaml); in the database later (#16).
        var dataProtection = services.AddDataProtection().SetApplicationName("tropicast-dashboard");
        if (configuration["DataProtection:KeysDirectory"] is { Length: > 0 } keys)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        }

        services.AddIdentityCore<AppUser>(options =>
            {
                // Length is the only rule (NIST SP 800-63B): no composition or distinct-character requirements.
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
                IdentityStore.Configure(options.Stores);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = SessionOrBearer;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            // The desktop app sends a bearer token; the SPA relies on its cookie.
            .AddPolicyScheme(SessionOrBearer, null, options => options.ForwardDefaultSelector = context =>
                IsBearer(context.Request) ? IdentityConstants.BearerScheme : IdentityConstants.ApplicationScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options => options.BearerTokenExpiration = TimeSpan.FromMinutes(15))
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = cookieOptions.SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieOptions.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // An API: answer 401/403 instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context => SetStatus(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden);
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeader;
            options.Cookie.Name = cookieOptions.AntiforgeryCookieName;
            options.Cookie.SecurePolicy = cookieOptions.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddScoped<TenantAccess>();
        services.AddScoped<TokenService>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TenantRoleHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(TenantPolicies.Member, TenantPolicies.Require(MembershipRole.Owner, MembershipRole.Admin, MembershipRole.Broadcaster))
            .AddPolicy(TenantPolicies.Broadcaster, TenantPolicies.Require(MembershipRole.Owner, MembershipRole.Admin, MembershipRole.Broadcaster))
            .AddPolicy(TenantPolicies.Admin, TenantPolicies.Require(MembershipRole.Owner, MembershipRole.Admin))
            .AddPolicy(TenantPolicies.Owner, TenantPolicies.Require(MembershipRole.Owner));

        services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, _) => new ValueTask(TypedResults
                .Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts. Try again in a minute.")
                .ExecuteAsync(context.HttpContext));
            options.AddPolicy(AuthRateLimit, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PermitLimit, Window = limits.Window });
            });
        });
        return services;
    }

    /// <summary>Authentication, tenant resolution, antiforgery for cookie sessions, rate limits and authorization, in order.</summary>
    internal static WebApplication UseDashboardAuth(this WebApplication app)
    {
        app.Use(NormalizeBearerScheme);
        app.UseAuthentication();
        app.UseMiddleware<TenantAccessMiddleware>();
        app.Use(ValidateAntiforgeryForSessionsAsync);
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Unsafe API requests that carry the session cookie must echo the antiforgery token. Bearer requests carry no
    /// ambient credentials and are exempt.
    /// </summary>
    private static async Task ValidateAntiforgeryForSessionsAsync(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method;
        var sessionCookie = context.RequestServices.GetRequiredService<IOptions<AuthCookieOptions>>().Value.SessionCookieName;
        if (context.Request.Path.StartsWithSegments("/api") && context.Request.Cookies.ContainsKey(sessionCookie)
            && !IsBearer(context.Request)
            && !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)))
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: $"Missing or invalid antiforgery token. Get one from /api/v1/auth/antiforgery and send it in {AntiforgeryHeader}.")
                    .ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }

    /// <summary>
    /// Scheme names are case-insensitive (RFC 9110), but the bearer handler only reads "Bearer ": rewrite "bearer ",
    /// "BEARER " and the like before authentication.
    /// </summary>
    private static Task NormalizeBearerScheme(HttpContext context, RequestDelegate next)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (IsBearer(context.Request) && !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            context.Request.Headers.Authorization = "Bearer " + header["Bearer ".Length..];
        }
        return next(context);
    }

    /// <summary>Authentication scheme names are case-insensitive (RFC 9110).</summary>
    private static bool IsBearer(HttpRequest request)
        => request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    private static Task SetStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}
