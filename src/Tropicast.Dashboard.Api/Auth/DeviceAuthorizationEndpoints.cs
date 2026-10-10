using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

internal sealed record DeviceCodeRequest([property: Required, MaxLength(64)] string DeviceName);
internal sealed record DeviceTokenRequest([property: Required, MaxLength(256)] string DeviceCode);
internal sealed record UserCodeRequest([property: Required, MaxLength(32)] string UserCode);

/// <summary>A started desktop sign-in. Show <see cref="UserCode"/> and <see cref="VerificationUri"/>, then poll.</summary>
/// <param name="DeviceCode">Secret: send it to <c>/auth/device/token</c> only. Never show it.</param>
/// <param name="UserCode">Short code the user types on the web page, e.g. <c>BCDF-GHJK</c>.</param>
/// <param name="VerificationUri">Web page where the user enters the code.</param>
/// <param name="VerificationUriComplete">The same page with the code filled in, e.g. for a button or a QR code.</param>
/// <param name="ExpiresIn">Seconds until the codes expire.</param>
/// <param name="Interval">Minimum seconds between two polls.</param>
internal sealed record DeviceCodeResponse(string DeviceCode, string UserCode, Uri VerificationUri, Uri VerificationUriComplete,
    long ExpiresIn, long Interval);

/// <summary>A pending desktop sign-in, shown to the user before they approve it.</summary>
/// <param name="UserCode">The code, as the device shows it.</param>
/// <param name="DeviceName">Name the device gave itself; it becomes the device's name in the account.</param>
/// <param name="CreatedAt">When the device asked.</param>
/// <param name="ExpiresAt">When the request expires.</param>
internal sealed record DeviceRequestResponse(string UserCode, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Desktop sign-in without typing a password in the app (OAuth 2.0 device authorization style, RFC 8628): the app
/// gets a code pair, the user approves the short code on the web while signed in, and the app's polls then return the
/// same tokens as <c>/auth/token</c>.
/// </summary>
internal static class DeviceAuthorizationEndpoints
{
    /// <summary>The <c>error</c> member of a failed poll, as in RFC 8628 section 3.5.</summary>
    internal static class Errors
    {
        internal const string Pending = "authorization_pending";
        internal const string SlowDown = "slow_down";
        internal const string Denied = "access_denied";
        internal const string Expired = "expired_token";
    }

    private const string NotFoundTitle = "This code does not exist or has expired. Start the sign-in again in the app.";

    internal static void MapDeviceAuthorization(this IEndpointRouteBuilder app)
    {
        var device = app.MapGroup("/api/v1/auth/device").WithTags("Auth").AddEndpointFilter<RequestValidation>();
        device.MapPost("/code", StartAsync).RequireRateLimiting(AuthSetup.AuthRateLimit)
            .WithSummary("Starts a desktop sign-in: a secret device code to poll with and a short user code to show.");
        // Polled every few seconds: the poll interval limits it, not the per-IP sign-in limit.
        device.MapPost("/token", PollAsync)
            .WithSummary("Polls a desktop sign-in; returns the device's tokens once the user approved it.");

        var signedIn = device.MapGroup("").RequireAuthorization().RequireRateLimiting(AuthSetup.AuthRateLimit);
        signedIn.MapGet("/{userCode}", GetAsync).WithSummary("A pending desktop sign-in, to confirm before approving it.");
        signedIn.MapPost("/approve", ApproveAsync).WithSummary("Signs the device with this code in to the signed-in account.");
        signedIn.MapPost("/deny", DenyAsync).WithSummary("Refuses the desktop sign-in with this code.");
    }

    private static async Task<Results<Ok<DeviceCodeResponse>, ValidationProblem>> StartAsync(DeviceCodeRequest request,
        AppDbContext db, IOptions<AppOptions> app, TimeProvider time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Trim().Length > 64)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["deviceName"] = ["Name the device in 1-64 characters, e.g. \"Studio PC\"."],
            });
        }
        var now = time.GetUtcNow();
        // Expired requests are useless, and deleting them keeps user codes unique among the live ones.
        await db.DeviceAuthorizations.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            var (pending, deviceCode) = DeviceAuthorization.Start(request.DeviceName, now);
            db.DeviceAuthorizations.Add(pending);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e) when (attempt < 3
                && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The random user code is taken (1 in 25 billion per live request): draw another.
                db.Entry(pending).State = EntityState.Detached;
                continue;
            }
            var page = new Uri(new Uri(app.Value.PublicBaseUrl), "/device");
            return TypedResults.Ok(new DeviceCodeResponse(deviceCode, pending.DisplayUserCode, page,
                new Uri($"{page}?code={pending.DisplayUserCode}"), (long)DeviceAuthorization.Lifetime.TotalSeconds,
                (long)DeviceAuthorization.PollInterval.TotalSeconds));
        }
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> PollAsync(DeviceTokenRequest request, HttpContext context,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, TokenService tokens, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var pending = DeviceAuthorization.TryGetId(request.DeviceCode, out var id)
            ? await db.DeviceAuthorizations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            : null;
        if (pending is null || !pending.Matches(request.DeviceCode))
        {
            return Failed(Errors.Expired, "Unknown or expired device code. Start the sign-in again.");
        }
        var state = pending.Poll(now);
        AppUser? user = null;
        DeviceSession? session = null;
        string? refreshToken = null;
        if (state == DeviceAuthorizationState.Approved)
        {
            user = await users.FindByIdAsync(pending.UserId!.Value.ToString());
            if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            {
                state = DeviceAuthorizationState.Denied;
            }
            else
            {
                (session, refreshToken) = DeviceSession.Start(user.Id, pending.DeviceName, now);
                db.DeviceSessions.Add(session);
            }
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another poll with the same code won the race: it got the tokens, or it was too soon.
            return state == DeviceAuthorizationState.Approved
                ? Failed(Errors.Expired, "This sign-in was already used. Start the sign-in again.")
                : Failed(Errors.SlowDown, "Polling too fast.");
        }
        context.Response.Headers.CacheControl = "no-store";
        return state switch
        {
            DeviceAuthorizationState.Approved => TypedResults.Ok(await tokens.IssueAsync(user!, session!, refreshToken!)),
            DeviceAuthorizationState.Pending => Failed(Errors.Pending, "Waiting for the user to approve the code."),
            DeviceAuthorizationState.SlowDown => Failed(Errors.SlowDown,
                $"Polling too fast: wait at least {DeviceAuthorization.PollInterval.TotalSeconds:0} seconds between polls."),
            DeviceAuthorizationState.Denied => Failed(Errors.Denied, "The sign-in was refused."),
            _ => Failed(Errors.Expired, "The code expired. Start the sign-in again."),
        };
    }

    private static async Task<Results<Ok<DeviceRequestResponse>, ProblemHttpResult>> GetAsync(string userCode, AppDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var pending = await PendingAsync(db, userCode, time.GetUtcNow(), cancellationToken);
        return pending is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: NotFoundTitle)
            : TypedResults.Ok(new DeviceRequestResponse(pending.DisplayUserCode, pending.DeviceName, pending.CreatedAt, pending.ExpiresAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ApproveAsync(UserCodeRequest request, HttpContext context,
        UserManager<AppUser> users, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(users.GetUserId(context.User)!);
        return await DecideAsync(db, request.UserCode, time.GetUtcNow(), (r, now) => r.Approve(userId, now), cancellationToken);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DenyAsync(UserCodeRequest request, AppDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
        => await DecideAsync(db, request.UserCode, time.GetUtcNow(), (r, now) => r.Deny(now), cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> DecideAsync(AppDbContext db, string userCode, DateTimeOffset now,
        Action<DeviceAuthorization, DateTimeOffset> decide, CancellationToken cancellationToken)
    {
        var pending = await PendingAsync(db, userCode, now, cancellationToken);
        if (pending is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: NotFoundTitle);
        }
        decide(pending, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Approved or denied at the same moment elsewhere: the first decision stands.
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: NotFoundTitle);
        }
        return TypedResults.NoContent();
    }

    private static async Task<DeviceAuthorization?> PendingAsync(AppDbContext db, string userCode, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var code = DeviceAuthorization.NormalizeUserCode(userCode);
        var pending = code is null ? null : await db.DeviceAuthorizations.SingleOrDefaultAsync(r => r.UserCode == code, cancellationToken);
        return pending is not null && pending.IsPending(now) ? pending : null;
    }

    private static ProblemHttpResult Failed(string error, string title) => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest, title: title, extensions: new Dictionary<string, object?> { ["error"] = error });
}
