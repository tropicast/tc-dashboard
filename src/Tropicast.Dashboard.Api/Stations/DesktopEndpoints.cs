using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>A station the signed-in user can broadcast to.</summary>
/// <param name="TenantId">Send it as <c>X-Tenant-Id</c> when asking for the broadcast target.</param>
/// <param name="TenantName">The station's tenant.</param>
/// <param name="StationId">Station ID in the API.</param>
/// <param name="PublicId">Station ID in mounts and listener URLs.</param>
/// <param name="Name">Station name.</param>
/// <param name="Role">The user's role in the tenant.</param>
internal sealed record MyStationResponse(Guid TenantId, string TenantName, Guid StationId, string PublicId, string Name,
    MembershipRole Role);

/// <summary>One stream to publish.</summary>
/// <param name="Format">Stream format.</param>
/// <param name="ContentType">Send it as the source's <c>Content-Type</c>.</param>
/// <param name="IngestUrl">Where to publish (HTTP PUT, Icecast source protocol, TLS).</param>
/// <param name="ListenerUrl">Where listeners play it.</param>
internal sealed record BroadcastOutput(AudioFormat Format, string ContentType, Uri IngestUrl, Uri ListenerUrl);

/// <summary>
/// Everything the desktop app needs to go live on a station. Contract version 1 (docs/desktop-api.md); it never
/// contains node, admin or relay credentials.
/// </summary>
/// <param name="StationId">Station ID in the API.</param>
/// <param name="PublicId">Station ID in mounts and listener URLs.</param>
/// <param name="StationName">Station name, e.g. for the <c>Ice-Name</c> header.</param>
/// <param name="CredentialId">The device's broadcast credential, listed under the station's credentials.</param>
/// <param name="Username">Source username: the station's public ID.</param>
/// <param name="Password">Source password of this device. Shown only here: store it now. Asking again replaces it.</param>
/// <param name="MaxBitrateKbps">Highest bitrate the plan accepts, per stream.</param>
/// <param name="Outputs">Streams the plan allows, MP3 first.</param>
internal sealed record BroadcastTargetResponse(Guid StationId, string PublicId, string StationName, Guid CredentialId, string Username,
    string Password, int MaxBitrateKbps, IReadOnlyList<BroadcastOutput> Outputs);

/// <summary>
/// The desktop API: which stations the user can broadcast to, and where and how to publish. A desktop device signed
/// in with <c>/auth/device</c> or <c>/auth/token</c> gets its own broadcast credential per station, bound to its
/// device session: signing the device out revokes it.
/// </summary>
internal static class DesktopEndpoints
{
    internal static void MapDesktop(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/stations", ListMyStationsAsync).WithTags("Desktop").RequireAuthorization()
            .WithSummary("Stations the signed-in user can broadcast to, in every tenant.");
        app.MapPost("/api/v1/stations/{id:guid}/broadcast-target", GetBroadcastTargetAsync).WithTags("Desktop")
            .RequireAuthorization(TenantPolicies.Broadcaster)
            .WithSummary("Ingest URLs, username and a new password for this desktop device; replaces the device's previous one.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<List<MyStationResponse>>> ListMyStationsAsync(HttpContext context, UserManager<AppUser> users,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(users.GetUserId(context.User)!);
        // The user's stations span tenants, so the tenant filter is crossed here on purpose; deleted stations stay hidden.
        var stations = await (
                from m in db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
                join t in db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]) on m.TenantId equals t.Id
                join s in db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter]) on t.Id equals s.TenantId
                where m.UserId == userId && t.Status == TenantStatus.Active
                orderby t.Name, s.Name
                select new MyStationResponse(t.Id, t.Name, s.Id, s.PublicId, s.Name, m.Role))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(stations);
    }

    private static async Task<Results<Ok<BroadcastTargetResponse>, NotFound, ProblemHttpResult>> GetBroadcastTargetAsync(Guid id,
        HttpContext context, TenantAccess access, AppDbContext db, IOptions<StreamingOptions> streaming, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var session = Guid.TryParse(context.User.FindFirst(TokenService.DeviceSessionClaim)?.Value, out var sessionId)
            ? await db.DeviceSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            : null;
        if (session is null || session.UserId.ToString() != userId || !session.IsActive(now))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only a signed-in desktop device gets a broadcast target. Sign in with the desktop app.");
        }
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        if (tenant.Status != TenantStatus.Active)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This tenant is suspended: it cannot broadcast.");
        }
        var plan = await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
        var assignment = await db.StreamAssignments.SingleOrDefaultAsync(a => a.StationId == station.Id, cancellationToken);
        var mountBase = assignment?.MountBase ?? $"/stations/{station.PublicId}";

        var secret = Secrets.New();
        var credential = BroadcastCredential.CreateForDevice(station.Id, session.Id, session.DeviceName, Secrets.Hash(secret), now);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.BroadcastCredentials
            .Where(c => c.StationId == station.Id && c.DeviceSessionId == session.Id && c.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.RevokedAt, now), cancellationToken);
        db.BroadcastCredentials.Add(credential);
        Audit.Record(db, access, context.User, "credential.created", nameof(BroadcastCredential), credential.Id.ToString(),
            $"{station.PublicId}: {credential.DeviceLabel} (desktop)", now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This device asked for the same station twice at once. Try again.");
        }
        await transaction.CommitAsync(cancellationToken);

        var outputs = new[] { AudioFormat.Mp3, AudioFormat.Opus }
            .Where(plan.Allows)
            .Select(format =>
            {
                var mount = $"{mountBase}/live.{(format == AudioFormat.Mp3 ? "mp3" : "opus")}";
                return new BroadcastOutput(format, format == AudioFormat.Mp3 ? "audio/mpeg" : "audio/ogg",
                    new Uri(streaming.Value.IngestBaseUrl, mount), new Uri(streaming.Value.ListenerBaseUrl, mount));
            })
            .ToList();
        // The password must not be cached anywhere on the way back.
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new BroadcastTargetResponse(station.Id, station.PublicId, station.Name, credential.Id, station.PublicId,
            secret, plan.MaxBitrateKbps, outputs));
    }
}
