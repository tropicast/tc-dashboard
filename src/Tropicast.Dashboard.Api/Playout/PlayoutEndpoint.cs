using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Media;
using Tropicast.Dashboard.Api.SourceAuth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Media;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Playout;

/// <summary>What the playout worker plays, for every station with AutoDJ on (#107).</summary>
/// <param name="Version">Changes when a station, its encoding, credential or playlist changes; not when URLs are renewed.</param>
/// <param name="UrlsExpireAt">The track URLs stop working then: fetch again (without If-None-Match) before.</param>
/// <param name="Stations">One entry per station to play.</param>
internal sealed record PlayoutResponse(string Version, DateTimeOffset UrlsExpireAt, IReadOnlyList<PlayoutStation> Stations);

/// <summary>A station the worker publishes to.</summary>
/// <param name="PublicId">The station's public ID.</param>
/// <param name="Username">Source user name (the public ID).</param>
/// <param name="Password">The station's AutoDJ credential (accepted on its autodj mounts only).</param>
/// <param name="Outputs">One per format: mount and the encoding to match the live stream.</param>
/// <param name="Playlist">What to play.</param>
internal sealed record PlayoutStation(string PublicId, string Username, string Password, IReadOnlyList<PlayoutOutput> Outputs,
    PlayoutPlaylist Playlist);

/// <summary>One AutoDJ mount.</summary>
internal sealed record PlayoutOutput(string Format, string Mount, string ContentType, int BitrateKbps, int SampleRate, int Channels);

/// <summary>The station's default playlist, in play order (shuffle it when <see cref="Shuffle"/>).</summary>
internal sealed record PlayoutPlaylist(Guid Id, string Name, bool Shuffle, IReadOnlyList<PlayoutTrack> Tracks);

/// <summary>A track and a presigned URL to download it.</summary>
internal sealed record PlayoutTrack(Guid Id, string Title, string Artist, long DurationMs, TrackFormat Format, Uri Url);

/// <summary>
/// <c>GET /internal/playout/stations</c> for the Liquidsoap playout worker (tc-streaming#61). Like source auth: the
/// internal port only, HTTP Basic with the node credentials. The answer carries secrets: never cached.
/// Poll with <c>If-None-Match</c> (304 while nothing changed); fetch in full before <c>urlsExpireAt</c>.
/// </summary>
internal static class PlayoutEndpoint
{
    internal const string Path = "/internal/playout/stations";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapPlayout(this WebApplication app)
        => app.MapGet(Path, HandleAsync).ExcludeFromDescription().RequireRateLimiting(Auth.AuthSetup.SourceAuthRateLimit);

    private static async Task<IResult> HandleAsync(HttpContext context, AppDbContext db, IObjectStorage storage,
        IOptions<StorageOptions> storageOptions, IDataProtectionProvider protection, IOptions<SourceAuthOptions> options,
        IOptions<StreamingOptions> streaming, TimeProvider time, CancellationToken cancellationToken)
    {
        // The listener the connection arrived on: never the public one.
        if (context.Connection.LocalPort != options.Value.Port)
        {
            return Results.NotFound();
        }
        if (!SourceAuthEndpoint.NodeAuthenticated(context.Request, options.Value))
        {
            return Results.Unauthorized();
        }
        var now = time.GetUtcNow();
        var lifetime = storageOptions.Value.DownloadUrlLifetime;
        var stations = await StationsAsync(db, streaming.Value.Node, cancellationToken);
        var result = new List<PlayoutStation>();
        foreach (var station in stations)
        {
            if (!storage.IsConfigured)
            {
                break; // No bucket: nothing can be played (and nothing was uploaded).
            }
            var password = await CredentialAsync(db, protection, station.Id, now, cancellationToken);
            var playlist = new PlayoutPlaylist(station.Playlist.Id, station.Playlist.Name, station.Playlist.Shuffle,
                [.. station.Tracks.Select(t => new PlayoutTrack(t.Id, t.Title, t.Artist, t.DurationMs ?? 0, t.Format!.Value,
                    storage.PresignDownload(t.ObjectKey, lifetime)))]);
            result.Add(new PlayoutStation(station.PublicId, station.PublicId, password.Secret, station.Outputs, playlist));
            station.CredentialId = password.Id;
        }
        await db.SaveChangesAsync(cancellationToken);

        var version = Version(stations.Where(s => s.CredentialId is not null));
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ETag = $"\"{version}\"";
        if (context.Request.Headers.IfNoneMatch.ToString().Trim() == $"\"{version}\"")
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return Results.Json(new PlayoutResponse(version, now + lifetime, result), Json);
    }

    private sealed class Candidate
    {
        public required Guid Id { get; init; }
        public required string PublicId { get; init; }
        public required Playlist Playlist { get; init; }
        public required IReadOnlyList<Track> Tracks { get; init; }
        public required IReadOnlyList<PlayoutOutput> Outputs { get; init; }
        public Guid? CredentialId { get; set; }
    }

    /// <summary>
    /// Stations on the node with AutoDJ on and in the plan, an active tenant, a station within the plan's count (after a
    /// downgrade the newest ones stop, as in source auth) and at least one track in the default playlist.
    /// </summary>
    private static async Task<List<Candidate>> StationsAsync(AppDbContext db, string node, CancellationToken cancellationToken)
    {
        var onNode = db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(a => a.Node == node);
        var rows = await db.AutoDjSettings.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(a => a.Enabled && a.DefaultPlaylistId != null && a.Station.DeletedAt == null && onNode.Any(s => s.StationId == a.StationId))
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.Status == TenantStatus.Active),
                a => a.Station.TenantId, t => t.Id, (a, t) => new { a.StationId, a.Station.PublicId, a.Station.TenantId, a.Station.CreatedAt, a.DefaultPlaylistId, t.PlanId })
            .Join(db.Plans.Where(p => p.AutoDj), x => x.PlanId, p => p.Id, (x, p) => new { x.StationId, x.PublicId, x.TenantId, x.CreatedAt, x.DefaultPlaylistId, Plan = p })
            .OrderBy(x => x.PublicId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }
        var tenants = rows.Select(r => r.TenantId).Distinct().ToList();
        var siblings = await db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(s => tenants.Contains(s.TenantId))
            .Select(s => new { s.Id, s.TenantId, s.CreatedAt }).ToListAsync(cancellationToken);
        var stationIds = rows.Select(r => r.StationId).ToList();
        var playlistIds = rows.Select(r => r.DefaultPlaylistId!.Value).ToList();
        var playlists = await db.Playlists.IgnoreQueryFilters([AppDbContext.TenantFilter]).Include(p => p.Items)
            .Where(p => playlistIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var trackIds = playlists.Values.SelectMany(p => p.Items.Select(i => i.TrackId)).Distinct().ToList();
        var tracks = await db.Tracks.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => trackIds.Contains(t.Id) && t.Status == TrackStatus.Ready).ToDictionaryAsync(t => t.Id, cancellationToken);
        var profiles = await db.EncodingProfiles.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(e => stationIds.Contains(e.StationId)).ToListAsync(cancellationToken);

        var result = new List<Candidate>();
        foreach (var row in rows)
        {
            var rank = siblings.Count(s => s.TenantId == row.TenantId
                && (s.CreatedAt < row.CreatedAt || (s.CreatedAt == row.CreatedAt && s.Id.CompareTo(row.StationId) < 0)));
            if (rank >= row.Plan.MaxStations || !playlists.TryGetValue(row.DefaultPlaylistId!.Value, out var playlist))
            {
                continue;
            }
            var entries = playlist.Items.OrderBy(i => i.Position).Where(i => tracks.ContainsKey(i.TrackId)).Select(i => tracks[i.TrackId]).ToList();
            if (entries.Count == 0)
            {
                continue;
            }
            var outputs = new[] { AudioFormat.Mp3, AudioFormat.Opus }.Where(row.Plan.Allows).Select(format =>
            {
                var profile = profiles.SingleOrDefault(p => p.StationId == row.StationId && p.Format == format)
                    ?? EncodingProfile.Default(row.StationId, format, row.Plan);
                var name = format == AudioFormat.Mp3 ? "mp3" : "opus";
                return new PlayoutOutput(name, $"/stations/{row.PublicId}/autodj.{name}", format == AudioFormat.Mp3 ? "audio/mpeg" : "audio/ogg",
                    profile.BitrateFor(row.Plan), profile.SampleRate, profile.Channels);
            }).ToList();
            result.Add(new Candidate { Id = row.StationId, PublicId = row.PublicId, Playlist = playlist, Tracks = entries, Outputs = outputs });
        }
        return result;
    }

    /// <summary>The station's AutoDJ secret; a credential that cannot be revealed (lost keys, none yet) is replaced.</summary>
    private static async Task<(Guid Id, string Secret)> CredentialAsync(AppDbContext db, IDataProtectionProvider protection, Guid stationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var credential = await AutoDjCredentials.EnsureAsync(db, protection, stationId, now, cancellationToken);
        if (AutoDjCredentials.Reveal(protection, credential) is { } secret)
        {
            return (credential.Id, secret);
        }
        credential.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        var fresh = await AutoDjCredentials.EnsureAsync(db, protection, stationId, now, cancellationToken);
        return (fresh.Id, AutoDjCredentials.Reveal(protection, fresh)!);
    }

    /// <summary>A hash of what the worker acts on: stations, credentials (by ID), outputs and playlists; never the URLs.</summary>
    private static string Version(IEnumerable<Candidate> stations)
    {
        var state = stations.Select(s => new
        {
            s.PublicId, s.CredentialId, s.Outputs,
            Playlist = new { s.Playlist.Id, s.Playlist.Name, s.Playlist.Shuffle },
            Tracks = s.Tracks.Select(t => new { t.Id, t.Title, t.Artist, t.DurationMs }),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Json))))[..16];
    }
}
