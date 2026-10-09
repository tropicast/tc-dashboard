using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

internal sealed record RegisterRequest([property: Required, EmailAddress, MaxLength(256)] string Email, [property: Required] string Password);
internal sealed record ConfirmEmailRequest([property: Required] Guid UserId, [property: Required] string Code);
internal sealed record EmailRequest([property: Required, MaxLength(256)] string Email);
internal sealed record LoginRequest([property: Required, MaxLength(256)] string Email, [property: Required] string Password);
internal sealed record ResetPasswordRequest([property: Required, MaxLength(256)] string Email, [property: Required] string Code,
    [property: Required] string NewPassword);
internal sealed record ChangePasswordRequest([property: Required] string CurrentPassword, [property: Required] string NewPassword);
internal sealed record TokenRequest([property: Required, MaxLength(256)] string Email, [property: Required] string Password,
    [property: Required, MaxLength(64)] string DeviceName);
internal sealed record RefreshRequest([property: Required, MaxLength(256)] string RefreshToken);
internal sealed record MembershipResponse(Guid TenantId, string TenantName, string TenantSlug, MembershipRole Role);
internal sealed record MeResponse(Guid Id, string Email, IReadOnlyList<MembershipResponse> Memberships);
internal sealed record DeviceResponse(Guid Id, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, bool Current);

/// <summary>
/// Accounts and sessions. The SPA signs in with a cookie (<c>/login</c>); the desktop app gets tokens
/// (<c>/token</c>). Answers never reveal whether an email address has an account.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>One answer for every failure (unknown email, wrong password, unconfirmed, locked), so none reveals an account.</summary>
    private const string SignInFailed =
        "Email or password is incorrect, the email address is not confirmed yet, or too many attempts failed. "
        + "Try again in 15 minutes or reset your password.";

    internal static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Auth").AddEndpointFilter<RequestValidation>();
        auth.MapGet("/antiforgery", GetAntiforgeryToken)
            .WithSummary("Sets the XSRF-TOKEN cookie; echo its value in X-XSRF-TOKEN on unsafe requests made with the session cookie.");

        var credentials = auth.MapGroup("").RequireRateLimiting(AuthSetup.AuthRateLimit);
        credentials.MapPost("/register", RegisterAsync).WithSummary("Creates an account and sends a confirmation email.");
        credentials.MapPost("/confirm-email", ConfirmEmailAsync).WithSummary("Confirms the email address with the code from the email.");
        credentials.MapPost("/resend-confirmation", ResendConfirmationAsync).WithSummary("Sends the confirmation email again.");
        credentials.MapPost("/login", LoginAsync).WithSummary("Signs the SPA in with the session cookie.");
        credentials.MapPost("/forgot-password", ForgotPasswordAsync).WithSummary("Emails a password reset link.");
        credentials.MapPost("/reset-password", ResetPasswordAsync).WithSummary("Sets a new password and signs out every desktop device.");
        credentials.MapPost("/token", IssueTokenAsync).WithSummary("Signs a desktop device in: access and refresh token.");
        credentials.MapPost("/token/refresh", RefreshTokenAsync).WithSummary("Exchanges a refresh token for a new pair; the old one stops working.");
        credentials.MapPost("/token/revoke", RevokeTokenAsync).WithSummary("Signs the device out.");

        var signedIn = auth.MapGroup("").RequireAuthorization();
        signedIn.MapPost("/logout", LogoutAsync).WithSummary("Ends the cookie session.");
        signedIn.MapPost("/change-password", ChangePasswordAsync).WithSummary("Changes the password and signs out every desktop device.");
        signedIn.MapGet("/me", GetMeAsync).WithSummary("The signed-in account and its tenants.");
        signedIn.MapGet("/devices", ListDevicesAsync).WithSummary("Signed-in desktop devices.");
        signedIn.MapDelete("/devices/{id:guid}", RevokeDeviceAsync).WithSummary("Signs one desktop device out.");
    }

    private static NoContent GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(AuthSetup.AntiforgeryRequestCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict, Path = "/",
        });
        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted, ValidationProblem>> RegisterAsync(RegisterRequest request,
        UserManager<AppUser> users, IEmailSender email, IOptions<AppOptions> app, TimeProvider time, CancellationToken cancellationToken)
    {
        var existing = await users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            await email.SendAsync(AuthEmails.AccountExists(existing.Email!, app.Value.PublicBaseUrl), cancellationToken);
            return TypedResults.Accepted((string?)null);
        }
        var user = new AppUser { UserName = request.Email, Email = request.Email, CreatedAt = time.GetUtcNow() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Invalid(result);
        }
        var code = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(AuthEmails.ConfirmEmail(user.Email!, app.Value.PublicBaseUrl, user.Id, code), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ConfirmEmailAsync(ConfirmEmailRequest request, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var code = AuthEmails.Decode(request.Code);
        if (user is null || code is null || !(await users.ConfirmEmailAsync(user, code)).Succeeded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The confirmation link is invalid or has expired.");
        }
        return TypedResults.NoContent();
    }

    private static async Task<Accepted> ResendConfirmationAsync(EmailRequest request, UserManager<AppUser> users,
        IEmailSender email, IOptions<AppOptions> app, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(request.Email) is { EmailConfirmed: false } user)
        {
            var code = await users.GenerateEmailConfirmationTokenAsync(user);
            await email.SendAsync(AuthEmails.ConfirmEmail(user.Email!, app.Value.PublicBaseUrl, user.Id, code), cancellationToken);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LoginAsync(LoginRequest request,
        UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: SignInFailed);
        }
        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        return result.Succeeded ? TypedResults.NoContent() : SignInProblem(result);
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Accepted> ForgotPasswordAsync(EmailRequest request, UserManager<AppUser> users,
        IEmailSender email, IOptions<AppOptions> app, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(request.Email) is { EmailConfirmed: true } user)
        {
            var code = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendAsync(AuthEmails.ResetPassword(user.Email!, app.Value.PublicBaseUrl, code), cancellationToken);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetPasswordAsync(ResetPasswordRequest request,
        UserManager<AppUser> users, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);
        var code = AuthEmails.Decode(request.Code);
        if (user is null || code is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The reset link is invalid or has expired.");
        }
        var result = await users.ResetPasswordAsync(user, code, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The reset link is invalid or has expired.")
                : Invalid(result);
        }
        await RevokeAllDevicesAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> ChangePasswordAsync(ChangePasswordRequest request,
        HttpContext context, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Invalid(result);
        }
        await RevokeAllDevicesAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
        // The security stamp changed: renew this browser's cookie so only other sessions end.
        if (context.Request.Cookies.ContainsKey(AuthSetup.SessionCookie))
        {
            await signIn.RefreshSignInAsync(user);
        }
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MeResponse>> GetMeAsync(HttpContext context, UserManager<AppUser> users, AppDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        // The user's tenants span tenants, so the tenant filter is crossed here on purpose.
        var memberships = await (
                from m in db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
                join t in db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]) on m.TenantId equals t.Id
                where m.UserId == user.Id
                orderby t.Name
                select new { t.Id, t.Name, t.Slug, m.Role })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new MeResponse(user.Id, user.Email!,
            [.. memberships.Select(m => new MembershipResponse(m.Id, m.Name, m.Slug, m.Role))]));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult, ValidationProblem>> IssueTokenAsync(TokenRequest request,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, TokenService tokens, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Trim().Length > 64)
        {
            return Invalid("deviceName", "Name the device in 1-64 characters, e.g. \"Studio PC\".");
        }
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: SignInFailed);
        }
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return SignInProblem(result);
        }
        var (session, refreshToken) = DeviceSession.Start(user.Id, request.DeviceName, time.GetUtcNow());
        db.DeviceSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tokens.IssueAsync(user, session, refreshToken));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshTokenAsync(RefreshRequest request,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, TokenService tokens, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var refused = TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in again on this device.");
        var now = time.GetUtcNow();
        var session = DeviceSession.TryGetSessionId(request.RefreshToken, out var sessionId)
            ? await db.DeviceSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            : null;
        if (session is null)
        {
            return refused;
        }
        if (!session.IsCurrent(request.RefreshToken))
        {
            // A token of this session that is no longer current was copied: end the session for every holder.
            session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return refused;
        }
        var user = await users.FindByIdAsync(session.UserId.ToString());
        if (!session.IsActive(now) || user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            return refused;
        }
        var refreshToken = session.Rotate(now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return refused;
        }
        return TypedResults.Ok(await tokens.IssueAsync(user, session, refreshToken));
    }

    private static async Task<NoContent> RevokeTokenAsync(RefreshRequest request, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (DeviceSession.TryGetSessionId(request.RefreshToken, out var sessionId)
            && await db.DeviceSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken) is { } session
            && session.IsCurrent(request.RefreshToken))
        {
            session.Revoke(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<DeviceResponse>>> ListDevicesAsync(HttpContext context, UserManager<AppUser> users,
        AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(users.GetUserId(context.User)!);
        var current = context.User.FindFirst(TokenService.DeviceSessionClaim)?.Value;
        var now = time.GetUtcNow();
        var sessions = await db.DeviceSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(sessions
            .Select(s => new DeviceResponse(s.Id, s.DeviceName, s.CreatedAt, s.LastUsedAt, s.Id.ToString() == current))
            .ToList());
    }

    private static async Task<Results<NoContent, NotFound>> RevokeDeviceAsync(Guid id, HttpContext context,
        UserManager<AppUser> users, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(users.GetUserId(context.User)!);
        var session = await db.DeviceSessions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
        if (session is null)
        {
            return TypedResults.NotFound();
        }
        session.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task RevokeAllDevicesAsync(AppDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await db.DeviceSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(cancellationToken);
        sessions.ForEach(s => s.Revoke(now));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lockout still applies server-side; the answer stays the same.</summary>
    private static ProblemHttpResult SignInProblem(SignInResult _) => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: SignInFailed);

    private static ValidationProblem Invalid(string field, string message)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static ValidationProblem Invalid(IdentityResult result)
        => TypedResults.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
