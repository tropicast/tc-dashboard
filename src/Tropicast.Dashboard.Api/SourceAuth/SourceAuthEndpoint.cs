using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.SourceAuth;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.SourceAuth;

/// <summary>Source-auth settings (<c>SourceAuth</c> section).</summary>
internal sealed class SourceAuthOptions
{
    /// <summary>
    /// The only port that serves the endpoint. Reachable from the streaming node over the private network,
    /// never routed by the public gateway.
    /// </summary>
    public int Port { get; set; } = 8081;
    /// <summary>Node credentials Icecast sends (ICECAST_SOURCE_AUTH_USER/_PASSWORD). Unset: every call gets 401.</summary>
    public string? NodeUsername { get; set; }
    public string? NodePassword { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// <c>POST /internal/icecast/source-auth</c>: Icecast asks before accepting audio from a source
/// (contract: tc-streaming docs/source-auth.md). Allow: 200 with <c>icecast-auth-user: 1</c>. Deny: 200 with
/// <c>icecast-auth-message</c>. Errors deny; reasons never contain the credential.
/// </summary>
internal static partial class SourceAuthEndpoint
{
    internal const string Path = "/internal/icecast/source-auth";

    internal static void MapSourceAuth(this WebApplication app)
    {
        var port = app.Services.GetRequiredService<IOptions<SourceAuthOptions>>().Value.Port;
        app.MapPost(Path, HandleAsync)
            .ExcludeFromDescription()
            .DisableAntiforgery();
        // Other internal paths: not found, never the SPA.
        app.Map("/internal/{**path}", () => Results.NotFound()).ExcludeFromDescription();
    }

    private static async Task HandleAsync(HttpContext context, IOptions<SourceAuthOptions> options, TimeProvider time,
        ILoggerFactory loggers)
    {
        // The listener the connection actually arrived on, not the Host header (which a client controls).
        if (context.Connection.LocalPort != options.Value.Port)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var logger = loggers.CreateLogger(typeof(SourceAuthEndpoint));
        if (!NodeAuthenticated(context.Request, options.Value))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        string? mount = null;
        SourceAuthDecision decision;
        try
        {
            // One budget for reading the form and deciding; anything going wrong denies.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(options.Value.Timeout);
            var request = await ReadAsync(context.Request, timeout.Token);
            mount = request.Mount;
            // Resolved here, so a missing or broken database denies instead of failing the request.
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            db.Database.SetCommandTimeout(options.Value.Timeout);
            decision = await DecideAndRecordAsync(request, db, time.GetUtcNow(), timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            // Fail closed: malformed input, a timeout or an outage must never let a source in.
            LogError(logger, ex.GetType().Name, mount);
            decision = SourceAuthDecision.Deny("temporary error, try again");
        }

        LogDecision(logger, decision.Allowed ? "allow" : "deny", mount, decision.Reason);
        context.Response.StatusCode = StatusCodes.Status200OK;
        if (decision.Allowed)
        {
            context.Response.Headers["icecast-auth-user"] = "1";
        }
        else
        {
            context.Response.Headers["icecast-auth-message"] = decision.Reason;
        }
    }

    /// <summary>Icecast sends a handful of short fields: anything larger is not Icecast.</summary>
    private static readonly FormOptions FormLimits = new()
    {
        ValueCountLimit = 64, KeyLengthLimit = 128, ValueLengthLimit = 4096, BufferBody = false,
    };

    private static async Task<SourceAuthRequest> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return new SourceAuthRequest(null, null, null, null, null);
        }
        var form = await new FormFeature(request, FormLimits).ReadFormAsync(cancellationToken);
        return new SourceAuthRequest(form["mount"], form["user"], form["pass"], form["header.ice-bitrate"], form["header.ice-audio-info"]);
    }

    private static async Task<SourceAuthDecision> DecideAndRecordAsync(SourceAuthRequest request, AppDbContext db,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Icecast calls without a tenant: the lookup crosses the tenant filter on purpose; deleted stations stay hidden.
        var publicId = SourceAuthorizer.StationOf(request.Mount);
        var station = publicId is null ? null : await db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => s.PublicId == publicId)
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]), s => s.TenantId, t => t.Id, (s, t) => new { s.Id, s.PublicId, t.Status, t.PlanId })
            .Join(db.Plans, x => x.PlanId, p => p.Id, (x, p) => new { x.Id, x.PublicId, x.Status, Plan = p })
            .SingleOrDefaultAsync(cancellationToken);
        var credentials = station is null ? [] : await db.BroadcastCredentials.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(c => c.StationId == station.Id && c.RevokedAt == null)
            // A desktop device's own credential ends with its device session: signed out, revoked or expired.
            .Where(c => c.DeviceSessionId == null
                || db.DeviceSessions.Any(d => d.Id == c.DeviceSessionId && d.RevokedAt == null && d.ExpiresAt > now))
            .Select(c => new { c.Id, c.SecretHash })
            .ToListAsync(cancellationToken);
        var decision = SourceAuthorizer.Decide(request, station is null ? null : new SourceAuthStation(station.PublicId,
            station.Status == TenantStatus.Active, station.Plan, [.. credentials.Select(c => (c.Id, c.SecretHash))]));
        if (!decision.Allowed)
        {
            return decision;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Re-check atomically: a credential revoked since the snapshot above must not be let in.
        var stillActive = await db.BroadcastCredentials.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(c => c.Id == decision.CredentialId && c.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.LastUsedAt, now), cancellationToken);
        if (stillActive == 0)
        {
            return SourceAuthDecision.Deny("wrong or revoked credential");
        }
        // Icecast refuses a second source on a live mount, so an open session here is stale (e.g. after a node restart).
        var stale = await db.LiveSessions.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => s.StationId == station!.Id && s.Format == decision.Format && s.EndedAt == null)
            .ToListAsync(cancellationToken);
        stale.ForEach(s => s.End(now));
        if (stale.Count > 0)
        {
            // The partial unique index allows one open session: close the stale ones first.
            await db.SaveChangesAsync(cancellationToken);
        }
        db.LiveSessions.Add(LiveSession.Start(station!.Id, decision.Format!.Value, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    /// <summary>HTTP Basic with the node credentials, compared in constant time over fixed-length hashes.</summary>
    private static bool NodeAuthenticated(HttpRequest request, SourceAuthOptions options)
    {
        if (string.IsNullOrEmpty(options.NodeUsername) || string.IsNullOrEmpty(options.NodePassword))
        {
            return false;
        }
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        string presented;
        try
        {
            presented = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = $"{options.NodeUsername}:{options.NodePassword}";
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Source auth {Result} for {Mount}: {Reason}")]
    private static partial void LogDecision(ILogger logger, string result, string? mount, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source auth failed ({ErrorType}) for {Mount}; denied")]
    private static partial void LogError(ILogger logger, string errorType, string? mount);
}
