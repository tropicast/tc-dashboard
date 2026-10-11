using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.Public;

/// <summary>A stream listeners can play. Audio goes from the listener host to the player, never through the API.</summary>
/// <param name="Format">Stream format.</param>
/// <param name="ContentType">MIME type, for the player's format check.</param>
/// <param name="Url">HTTPS listener URL.</param>
internal sealed record PublicStreamResponse(AudioFormat Format, string ContentType, Uri Url);

/// <summary>What anyone may see about a station. No tenant, plan or listener data.</summary>
/// <param name="Name">Station name.</param>
/// <param name="Description">Description.</param>
/// <param name="Genre">Genre.</param>
/// <param name="Country">ISO 3166-1 alpha-2 code.</param>
/// <param name="Language">BCP 47 language tag.</param>
/// <param name="LogoUrl">Logo address.</param>
/// <param name="Website">The station's website.</param>
/// <param name="Live">On air now; null when not known.</param>
/// <param name="Streams">Streams, Opus first (less data) when the plan includes it, then MP3 (plays everywhere).</param>
/// <param name="PageUrl">Public page.</param>
/// <param name="EmbedUrl">Embeddable player, or null when the station's plan does not include embedding.</param>
internal sealed record PublicStationResponse(string Name, string Description, string Genre, string? Country, string? Language, Uri? LogoUrl,
    Uri? Website, bool? Live, IReadOnlyList<PublicStreamResponse> Streams, Uri PageUrl, Uri? EmbedUrl);

/// <summary>
/// Public station page, embeddable player and their read endpoint (#12). Rendered by the API without the SPA, so the
/// page is a few kilobytes and search engines and link previews see its title and Open Graph tags.
/// Station slugs are unique per tenant, so public addresses name both: <c>/listen/{tenant}/{station}</c>.
/// </summary>
internal static class PublicStationEndpoints
{
    /// <summary>Live status changes; let browsers and proxies reuse an answer briefly.</summary>
    private const string ApiCache = "public, max-age=15, stale-while-revalidate=30";
    private const string PageCache = "public, max-age=30, stale-while-revalidate=60";

    internal static void MapPublicStations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/public/stations/{tenantSlug}/{stationSlug}", GetAsync).WithTags("Public").AllowAnonymous()
            .RequireRateLimiting(AuthSetup.PublicRateLimit)
            .WithSummary("A station's public information, live state and listener streams. Cacheable; no tenant data.");
        app.MapGet("/listen/{tenantSlug}/{stationSlug}", PageAsync).AllowAnonymous().ExcludeFromDescription()
            .RequireRateLimiting(AuthSetup.PublicRateLimit);
        app.MapGet("/embed/{tenantSlug}/{stationSlug}", EmbedAsync).AllowAnonymous().ExcludeFromDescription()
            .RequireRateLimiting(AuthSetup.PublicRateLimit);
        app.MapGet("/embed.js", EmbedScript).AllowAnonymous().ExcludeFromDescription();
    }

    private static async Task<Results<Ok<PublicStationResponse>, NotFound>> GetAsync(string tenantSlug, string stationSlug, HttpContext context,
        AppDbContext db, LiveStatusStore store, IOptions<StatsOptions> stats, IOptions<StreamingOptions> streaming, IOptions<AppOptions> app,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var found = await FindAsync(db, tenantSlug, stationSlug, cancellationToken);
        if (found is null)
        {
            return TypedResults.NotFound();
        }
        context.Response.Headers.CacheControl = ApiCache;
        // Readable from other sites (no credentials involved).
        context.Response.Headers.AccessControlAllowOrigin = "*";
        return TypedResults.Ok(ToResponse(found, tenantSlug, store, stats.Value, streaming.Value, app.Value, time.GetUtcNow()));
    }

    private static async Task<IResult> PageAsync(string tenantSlug, string stationSlug, HttpContext context, AppDbContext db,
        LiveStatusStore store, IOptions<StatsOptions> stats, IOptions<StreamingOptions> streaming, IOptions<AppOptions> app,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var found = await FindAsync(db, tenantSlug, stationSlug, cancellationToken);
        if (found is null)
        {
            return Html(context, PublicPages.NotFound(Nonce(context)), StatusCodes.Status404NotFound, embeddable: false, streaming.Value);
        }
        var station = ToResponse(found, tenantSlug, store, stats.Value, streaming.Value, app.Value, time.GetUtcNow());
        context.Response.Headers.CacheControl = PageCache;
        return Html(context, PublicPages.Station(station, StatusUrl(tenantSlug, stationSlug), Nonce(context), embed: false),
            StatusCodes.Status200OK, embeddable: false, streaming.Value);
    }

    private static async Task<IResult> EmbedAsync(string tenantSlug, string stationSlug, HttpContext context, AppDbContext db,
        LiveStatusStore store, IOptions<StatsOptions> stats, IOptions<StreamingOptions> streaming, IOptions<AppOptions> app,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var found = await FindAsync(db, tenantSlug, stationSlug, cancellationToken);
        if (found is null)
        {
            return Html(context, PublicPages.NotFound(Nonce(context)), StatusCodes.Status404NotFound, embeddable: true, streaming.Value);
        }
        var station = ToResponse(found, tenantSlug, store, stats.Value, streaming.Value, app.Value, time.GetUtcNow());
        if (!found.Plan.Embed)
        {
            return Html(context, PublicPages.EmbedNotIncluded(station, Nonce(context)), StatusCodes.Status403Forbidden, embeddable: true,
                streaming.Value);
        }
        context.Response.Headers.CacheControl = PageCache;
        return Html(context, PublicPages.Station(station, StatusUrl(tenantSlug, stationSlug), Nonce(context), embed: true),
            StatusCodes.Status200OK, embeddable: true, streaming.Value);
    }

    private static ContentHttpResult EmbedScript(HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=3600";
        return TypedResults.Text(PublicPages.EmbedScript, "text/javascript; charset=utf-8");
    }

    private sealed record Found(Station Station, Plan Plan, string? Node);

    /// <summary>The station of an active tenant, across tenants on purpose; deleted stations stay hidden.</summary>
    private static async Task<Found?> FindAsync(AppDbContext db, string tenantSlug, string stationSlug, CancellationToken cancellationToken)
    {
        if (!Slugs.IsValid(tenantSlug) || !Slugs.IsValid(stationSlug))
        {
            return null;
        }
        var tenants = db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]);
        var stations = db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter]);
        var assignments = db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter]);
        var row = await (
                from t in tenants
                join s in stations on t.Id equals s.TenantId
                join p in db.Plans on t.PlanId equals p.Id
                where t.Slug == tenantSlug && s.Slug == stationSlug && t.Status == TenantStatus.Active
                select new
                {
                    Station = s,
                    Plan = p,
                    Node = assignments.Where(a => a.StationId == s.Id).Select(a => a.Node).FirstOrDefault(),
                })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new Found(row.Station, row.Plan, row.Node);
    }

    private static PublicStationResponse ToResponse(Found found, string tenantSlug, LiveStatusStore store, StatsOptions stats,
        StreamingOptions streaming, AppOptions app, DateTimeOffset now)
    {
        var station = found.Station;
        var (live, _, _) = LiveStatus.Of(store, found.Node ?? streaming.Node, station.PublicId, stats.Interval, now);
        var mountBase = $"/stations/{station.PublicId}";
        List<PublicStreamResponse> streams = [];
        if (found.Plan.Allows(AudioFormat.Opus))
        {
            streams.Add(new(AudioFormat.Opus, "audio/ogg", new Uri(streaming.ListenerBaseUrl, $"{mountBase}/live.opus")));
        }
        streams.Add(new(AudioFormat.Mp3, "audio/mpeg", new Uri(streaming.ListenerBaseUrl, $"{mountBase}/live.mp3")));
        var origin = new Uri(app.PublicBaseUrl);
        return new PublicStationResponse(station.Name, station.Description, station.Genre, station.Country, station.Language,
            station.LogoUrl, station.Website, live, streams, new Uri(origin, $"/listen/{tenantSlug}/{station.Slug}"),
            found.Plan.Embed ? new Uri(origin, $"/embed/{tenantSlug}/{station.Slug}") : null);
    }

    private static string StatusUrl(string tenantSlug, string stationSlug) => $"/api/v1/public/stations/{tenantSlug}/{stationSlug}";

    private static string Nonce(HttpContext context)
        => context.Items["csp-nonce"] as string ?? (string)(context.Items["csp-nonce"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));

    /// <summary>
    /// Strict CSP: only this page's inline code (nonce), images over HTTPS, audio from the listener host, status
    /// from this origin. The page cannot be framed; the embed can be framed anywhere.
    /// </summary>
    private static ContentHttpResult Html(HttpContext context, string html, int status, bool embeddable, StreamingOptions streaming)
    {
        var nonce = Nonce(context);
        var listen = streaming.ListenerBaseUrl.GetLeftPart(UriPartial.Authority);
        context.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; img-src https: data:; media-src {listen}; "
            + $"connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors {(embeddable ? "*" : "'none'")}";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (!embeddable)
        {
            context.Response.Headers.XFrameOptions = "DENY";
        }
        return TypedResults.Content(html, "text/html; charset=utf-8", statusCode: status);
    }
}
