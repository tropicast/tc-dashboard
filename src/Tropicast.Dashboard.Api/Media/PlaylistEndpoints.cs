using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Media;

/// <summary>A playlist in a list.</summary>
/// <param name="Id">Playlist ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Shuffle">Plays in random order.</param>
/// <param name="TrackCount">Entries.</param>
/// <param name="DurationMs">Playing time of all entries.</param>
/// <param name="UpdatedAt">Last change.</param>
internal sealed record PlaylistSummaryResponse(Guid Id, string Name, bool Shuffle, int TrackCount, long DurationMs, DateTimeOffset UpdatedAt);

/// <summary>An entry of a playlist.</summary>
/// <param name="TrackId">The track.</param>
/// <param name="Title">Its title.</param>
/// <param name="Artist">Its artist; may be empty.</param>
/// <param name="DurationMs">Its playing time.</param>
internal sealed record PlaylistTrackResponse(Guid TrackId, string Title, string Artist, long DurationMs);

/// <summary>A playlist with its entries in play order.</summary>
/// <param name="Id">Playlist ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Shuffle">Plays in random order.</param>
/// <param name="Tracks">Entries in play order.</param>
/// <param name="DurationMs">Playing time of all entries.</param>
/// <param name="UpdatedAt">Last change.</param>
internal sealed record PlaylistResponse(Guid Id, string Name, bool Shuffle, IReadOnlyList<PlaylistTrackResponse> Tracks, long DurationMs,
    DateTimeOffset UpdatedAt);

/// <summary>A station's AutoDJ.</summary>
/// <param name="Enabled">Turned on (it plays only while <paramref name="Available"/>).</param>
/// <param name="DefaultPlaylistId">The playlist it plays.</param>
/// <param name="Available">The plan includes AutoDJ.</param>
/// <param name="Active">Enabled and available: plays when no DJ is live.</param>
/// <param name="Scheduling">The plan includes scheduled playlists (not offered yet).</param>
/// <param name="Encoding">What AutoDJ encodes per format, pinned from the live source (or the defaults until one connected).</param>
internal sealed record AutoDjResponse(bool Enabled, Guid? DefaultPlaylistId, bool Available, bool Active, bool Scheduling,
    IReadOnlyList<EncodingResponse> Encoding);

/// <summary>An encoding, as the live source declared it at its last connect.</summary>
/// <param name="Format">mp3 or opus.</param>
/// <param name="BitrateKbps">Bitrate, capped by the plan.</param>
/// <param name="SampleRate">Hz.</param>
/// <param name="Channels">1 or 2.</param>
/// <param name="Pinned">From a live source; false for the defaults.</param>
internal sealed record EncodingResponse(string Format, int BitrateKbps, int SampleRate, int Channels, bool Pinned);

/// <summary>
/// Playlists of a station and its AutoDJ settings (#105). Stations of other tenants answer 404. Playlist changes
/// take an optional <c>If-Match</c>; turning AutoDJ on needs a plan with AutoDJ and a non-empty playlist.
/// </summary>
internal static class PlaylistEndpoints
{
    internal static void MapPlaylists(this IEndpointRouteBuilder app)
    {
        var station = app.MapGroup("/api/v1/stations/{stationId:guid}").WithTags("AutoDJ").AddEndpointFilter<CommandValidation>();
        var playlists = station.MapGroup("/playlists").RequireAuthorization(TenantPolicies.Member);
        playlists.MapGet("", ListAsync).WithSummary("The station's playlists.");
        playlists.MapPost("", CreateAsync).WithETag().WithSummary("Creates an empty playlist.");
        playlists.MapGet("/{playlistId:guid}", GetAsync).WithETag().WithSummary("A playlist and its tracks in play order.");
        playlists.MapPatch("/{playlistId:guid}", UpdateAsync).WithIfMatch(required: false).WithETag()
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).WithSummary("Renames a playlist or changes its order mode.");
        playlists.MapPut("/{playlistId:guid}/tracks", SetTracksAsync).WithIfMatch(required: false).WithETag()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .WithSummary("Replaces the playlist's tracks, in play order (add, remove and reorder in one call).");
        playlists.MapDelete("/{playlistId:guid}", DeleteAsync).WithIfMatch(required: false)
            .ProducesProblem(StatusCodes.Status409Conflict).WithSummary("Deletes a playlist (not the one AutoDJ is playing).");
        station.MapGet("/autodj", GetAutoDjAsync).RequireAuthorization(TenantPolicies.Member)
            .WithSummary("Whether AutoDJ is on, its playlist, and whether the plan includes it.");
        station.MapPut("/autodj", UpdateAutoDjAsync).RequireAuthorization(TenantPolicies.Admin)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Turns AutoDJ on or off and picks its playlist.");
    }

    private static async Task<Results<Ok<List<PlaylistSummaryResponse>>, NotFound>> ListAsync(Guid stationId, AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var playlists = await db.Playlists.Where(p => p.StationId == stationId).OrderBy(p => p.Name)
            .Select(p => new PlaylistSummaryResponse(p.Id, p.Name, p.Shuffle, p.Items.Count,
                p.Items.Sum(i => i.Track.DurationMs ?? 0), p.UpdatedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(playlists);
    }

    private static async Task<Results<Created<PlaylistResponse>, NotFound>> CreateAsync(Guid stationId, CreatePlaylistCommand command,
        HttpContext context, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var playlist = Playlist.Create(stationId, command.Name, command.Shuffle, time.GetUtcNow());
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);
        Concurrency.SetETag(context, db, playlist);
        return TypedResults.Created($"/api/v1/stations/{stationId}/playlists/{playlist.Id}", await ResponseAsync(db, playlist, cancellationToken));
    }

    private static async Task<Results<Ok<PlaylistResponse>, NotFound>> GetAsync(Guid stationId, Guid playlistId, HttpContext context,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var playlist = await FindAsync(db, stationId, playlistId, cancellationToken);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }
        Concurrency.SetETag(context, db, playlist);
        return TypedResults.Ok(await ResponseAsync(db, playlist, cancellationToken));
    }

    private static async Task<Results<Ok<PlaylistResponse>, NotFound, ProblemHttpResult>> UpdateAsync(Guid stationId, Guid playlistId,
        UpdatePlaylistCommand command, HttpContext context, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var playlist = await FindAsync(db, stationId, playlistId, cancellationToken);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(context, db, playlist, required: false) is { } precondition)
        {
            return precondition;
        }
        var now = time.GetUtcNow();
        if (command.Name is { } name)
        {
            playlist.Rename(name, now);
        }
        if (command.Shuffle is { } shuffle)
        {
            playlist.SetShuffle(shuffle, now);
        }
        if (await Concurrency.SaveAsync(db, cancellationToken) is { } stale)
        {
            return stale;
        }
        Concurrency.SetETag(context, db, playlist);
        return TypedResults.Ok(await ResponseAsync(db, playlist, cancellationToken));
    }

    private static async Task<Results<Ok<PlaylistResponse>, NotFound, ValidationProblem, ProblemHttpResult>> SetTracksAsync(Guid stationId,
        Guid playlistId, SetPlaylistTracksCommand command, HttpContext context, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var playlist = await FindAsync(db, stationId, playlistId, cancellationToken);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(context, db, playlist, required: false) is { } precondition)
        {
            return precondition;
        }
        var wanted = command.TrackIds.Distinct().ToList();
        var known = await db.Tracks.Where(t => t.StationId == stationId && t.Status == TrackStatus.Ready && wanted.Contains(t.Id))
            .CountAsync(cancellationToken);
        if (known != wanted.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["trackIds"] = ["Only this station's uploaded tracks can be added."],
            });
        }
        if (command.TrackIds.Count == 0 && await PlayingAsync(db, stationId, playlistId, cancellationToken))
        {
            return Conflict("AutoDJ plays this playlist: turn AutoDJ off or choose another playlist before emptying it.");
        }
        // The old entries go with their rows; the new ones get new rows in the given order.
        db.PlaylistItems.RemoveRange(playlist.Items);
        playlist.SetTracks(command.TrackIds, time.GetUtcNow());
        db.PlaylistItems.AddRange(playlist.Items);
        if (await Concurrency.SaveAsync(db, cancellationToken) is { } stale)
        {
            return stale;
        }
        Concurrency.SetETag(context, db, playlist);
        return TypedResults.Ok(await ResponseAsync(db, playlist, cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(Guid stationId, Guid playlistId,
        HttpContext context, AppDbContext db, CancellationToken cancellationToken)
    {
        var playlist = await FindAsync(db, stationId, playlistId, cancellationToken);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(context, db, playlist, required: false) is { } precondition)
        {
            return precondition;
        }
        if (await PlayingAsync(db, stationId, playlistId, cancellationToken))
        {
            return Conflict("AutoDJ plays this playlist: turn AutoDJ off or choose another playlist before deleting it.");
        }
        db.Playlists.Remove(playlist);
        return await Concurrency.SaveAsync(db, cancellationToken) is { } stale ? stale : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AutoDjResponse>, NotFound>> GetAutoDjAsync(Guid stationId, AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var settings = await db.AutoDjSettings.SingleOrDefaultAsync(a => a.StationId == stationId, cancellationToken);
        return TypedResults.Ok(await ResponseAsync(db, stationId, settings, await CurrentPlanAsync(db, cancellationToken), cancellationToken));
    }

    private static async Task<Results<Ok<AutoDjResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAutoDjAsync(
        Guid stationId, UpdateAutoDjCommand command, HttpContext context, AppDbContext db, TenantAccess access,
        IDataProtectionProvider protection, IOptions<StreamingOptions> streaming, ILoggerFactory loggers, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var plan = await CurrentPlanAsync(db, cancellationToken);
        if (command.Enabled && !plan.AutoDj)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: AutoDjRules.NotInPlan(plan),
                extensions: new Dictionary<string, object?> { ["limit"] = "autodj" });
        }
        if (command.DefaultPlaylistId is { } playlistId
            && !await db.Playlists.AnyAsync(p => p.Id == playlistId && p.StationId == stationId, cancellationToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["defaultPlaylistId"] = ["Choose one of this station's playlists."],
            });
        }
        if (command.Enabled && (command.DefaultPlaylistId is not { } chosen
            || !await db.PlaylistItems.AnyAsync(i => i.PlaylistId == chosen, cancellationToken)))
        {
            return Conflict(AutoDjRules.NeedsPlaylist);
        }
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.AutoDjSettings.SingleOrDefaultAsync(a => a.StationId == stationId, cancellationToken);
        if (command.Enabled && settings is not { Enabled: true }
            && await NodeFullAsync(db, streaming.Value, plan, loggers.CreateLogger(typeof(PlaylistEndpoints)), cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "No streaming capacity is free for AutoDJ right now. The operator has been alerted; try again later.");
        }
        if (settings is null)
        {
            settings = AutoDjSettings.For(stationId, now);
            db.AutoDjSettings.Add(settings);
        }
        var wasEnabled = settings.Enabled;
        var previousPlaylist = settings.DefaultPlaylistId;
        settings.Set(command.Enabled, command.DefaultPlaylistId, now);
        // The playout worker publishes with the station's AutoDJ credential while AutoDJ is on, and with none after.
        if (settings.Enabled)
        {
            await AutoDjCredentials.EnsureAsync(db, protection, stationId, now, cancellationToken);
        }
        else
        {
            await AutoDjCredentials.RevokeAsync(db, stationId, now, cancellationToken);
        }
        if (wasEnabled != settings.Enabled)
        {
            Audit.Record(db, access, context.User, settings.Enabled ? "autodj.enabled" : "autodj.disabled", "Station",
                stationId.ToString(), null, now);
        }
        else if (previousPlaylist != settings.DefaultPlaylistId)
        {
            Audit.Record(db, access, context.User, "autodj.updated", "Station", stationId.ToString(), "default playlist", now);
        }
        if (await Concurrency.SaveAsync(db, cancellationToken) is { } stale)
        {
            return stale;
        }
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(await ResponseAsync(db, stationId, settings, plan, cancellationToken));
    }

    /// <summary>
    /// Whether the node lacks the sources for this station's AutoDJ mounts (one per format: they count toward Icecast's
    /// <c>&lt;sources&gt;</c> like live mounts). Locks the node for the transaction, as station creation does.
    /// </summary>
    private static async Task<bool> NodeFullAsync(AppDbContext db, StreamingOptions streaming, Plan plan, ILogger logger,
        CancellationToken cancellationToken)
    {
        await NodeLoad.LockAsync(db, streaming.Node, cancellationToken);
        var load = await NodeLoad.ReadAsync(db, streaming.Node, cancellationToken);
        var autoDj = await NodeLoad.AutoDjSourcesAsync(db, streaming.Node, cancellationToken) + NodeCapacity.SourcesOf(plan);
        var reason = NodeCapacity.WhyNot(load.Select(s => s.Plan), streaming.Limits, autoDj);
        if (reason is not null)
        {
            NodeLoad.LogNodeFull(logger, streaming.Node, reason);
        }
        return reason is not null;
    }

    /// <summary>Whether AutoDJ is on and plays this playlist.</summary>
    private static Task<bool> PlayingAsync(AppDbContext db, Guid stationId, Guid playlistId, CancellationToken cancellationToken)
        => db.AutoDjSettings.AnyAsync(a => a.StationId == stationId && a.Enabled && a.DefaultPlaylistId == playlistId, cancellationToken);

    private static Task<Playlist?> FindAsync(AppDbContext db, Guid stationId, Guid playlistId, CancellationToken cancellationToken)
        => db.Playlists.Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == playlistId && p.StationId == stationId, cancellationToken);

    private static async Task<PlaylistResponse> ResponseAsync(AppDbContext db, Playlist playlist, CancellationToken cancellationToken)
    {
        var ids = playlist.Items.Select(i => i.TrackId).Distinct().ToList();
        var tracks = await db.Tracks.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var entries = playlist.Items.OrderBy(i => i.Position).Where(i => tracks.ContainsKey(i.TrackId))
            .Select(i => tracks[i.TrackId])
            .Select(t => new PlaylistTrackResponse(t.Id, t.Title, t.Artist, t.DurationMs ?? 0))
            .ToList();
        return new PlaylistResponse(playlist.Id, playlist.Name, playlist.Shuffle, entries, entries.Sum(e => e.DurationMs), playlist.UpdatedAt);
    }

    private static async Task<AutoDjResponse> ResponseAsync(AppDbContext db, Guid stationId, AutoDjSettings? settings, Plan plan,
        CancellationToken cancellationToken)
    {
        var pinned = await db.EncodingProfiles.Where(e => e.StationId == stationId).ToListAsync(cancellationToken);
        var encoding = new[] { Domain.AudioFormat.Mp3, Domain.AudioFormat.Opus }.Where(plan.Allows)
            .Select(format => pinned.SingleOrDefault(e => e.Format == format) is { } profile
                ? new EncodingResponse(Name(format), profile.BitrateFor(plan), profile.SampleRate, profile.Channels, true)
                : Default(EncodingProfile.Default(stationId, format, plan)))
            .ToList();
        return new AutoDjResponse(settings?.Enabled ?? false, settings?.DefaultPlaylistId, plan.AutoDj,
            (settings?.Enabled ?? false) && plan.AutoDj, plan.AutoDjScheduling, encoding);

        static EncodingResponse Default(EncodingProfile profile)
            => new(Name(profile.Format), profile.BitrateKbps, profile.SampleRate, profile.Channels, false);
        static string Name(Domain.AudioFormat format) => format == Domain.AudioFormat.Mp3 ? "mp3" : "opus";
    }

    private static async Task<Plan> CurrentPlanAsync(AppDbContext db, CancellationToken cancellationToken)
        => await db.Tenants.Join(db.Plans, t => t.PlanId, p => p.Id, (_, p) => p).SingleAsync(cancellationToken);

    private static ProblemHttpResult Conflict(string title) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
