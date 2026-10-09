using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Application.Stations;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>A broadcast credential, without its secret.</summary>
/// <param name="Id">Credential ID.</param>
/// <param name="DeviceLabel">The device it was issued for.</param>
/// <param name="CreatedAt">When it was issued.</param>
/// <param name="LastUsedAt">Last accepted connection from Icecast; null if never used.</param>
/// <param name="RevokedAt">When it was revoked; null while active.</param>
internal sealed record CredentialResponse(Guid Id, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>A new credential with its secret. This response is the only time the secret is shown.</summary>
/// <param name="Credential">The stored credential.</param>
/// <param name="Username">Icecast source username: the station's public ID.</param>
/// <param name="Secret">Icecast source password. Store it in the device now; it cannot be shown again.</param>
internal sealed record IssuedCredentialResponse(CredentialResponse Credential, string Username, string Secret);

/// <summary>
/// Per-device broadcast credentials of a station. Customers never see a shared Icecast password; revoking one
/// device's credential leaves the others working, and takes effect on that device's next connect.
/// </summary>
internal static class CredentialEndpoints
{
    internal static void MapCredentials(this IEndpointRouteBuilder app)
    {
        var credentials = app.MapGroup("/api/v1/stations/{stationId:guid}/credentials").WithTags("Credentials")
            .AddEndpointFilter<CommandValidation>()
            .RequireAuthorization(TenantPolicies.Admin);
        credentials.MapGet("", ListAsync).WithSummary("Credentials of the station's devices, active first. Secrets are never listed.");
        credentials.MapPost("", CreateAsync).WithSummary("Issues a credential for one device and shows its secret once.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        credentials.MapDelete("/{credentialId:guid}", RevokeAsync).WithSummary("Revokes one device's credential.");
    }

    private static async Task<Results<Ok<List<CredentialResponse>>, NotFound>> ListAsync(Guid stationId, AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var credentials = await db.BroadcastCredentials
            .Where(c => c.StationId == stationId)
            .OrderBy(c => c.RevokedAt != null).ThenBy(c => c.DeviceLabel)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(credentials.Select(ToResponse).ToList());
    }

    private static async Task<Results<Created<IssuedCredentialResponse>, NotFound, ValidationProblem, ProblemHttpResult>> CreateAsync(
        Guid stationId, CreateCredentialCommand command, HttpContext context, TenantAccess access, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Id == stationId, cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        var now = time.GetUtcNow();
        var secret = Secrets.New();
        var credential = BroadcastCredential.Create(station.Id, command.DeviceLabel, Secrets.Hash(secret), now);
        db.BroadcastCredentials.Add(credential);
        Audit.Record(db, access, context.User, "credential.created", nameof(BroadcastCredential), credential.Id.ToString(),
            $"{station.PublicId}: {credential.DeviceLabel}", now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This station already has an active credential with this device label. Revoke it or choose another label.");
        }
        // The secret must not be cached anywhere on the way back.
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Created($"/api/v1/stations/{station.Id}/credentials/{credential.Id}",
            new IssuedCredentialResponse(ToResponse(credential), station.PublicId, secret));
    }

    /// <summary>
    /// Revokes with a conditional UPDATE, so concurrent revokes (or a concurrent last-used update) never fail:
    /// whichever request changes the row writes the audit entry, and every caller gets 204.
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> RevokeAsync(Guid stationId, Guid credentialId, HttpContext context,
        TenantAccess access, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var credentials = db.BroadcastCredentials
            .Where(c => c.Id == credentialId && c.StationId == stationId && c.Station.DeletedAt == null);
        var label = await credentials.Select(c => c.DeviceLabel).SingleOrDefaultAsync(cancellationToken);
        if (label is null)
        {
            return TypedResults.NotFound();
        }
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var revoked = await credentials.Where(c => c.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.RevokedAt, now), cancellationToken);
        if (revoked == 1)
        {
            Audit.Record(db, access, context.User, "credential.revoked", nameof(BroadcastCredential), credentialId.ToString(), label, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static CredentialResponse ToResponse(BroadcastCredential credential)
        => new(credential.Id, credential.DeviceLabel, credential.CreatedAt, credential.LastUsedAt, credential.RevokedAt);
}
