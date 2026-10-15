using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Media;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Playlists and the per-station AutoDJ switch (#105).</summary>
public sealed class PlaylistTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Browser Owner, StationResponse Station)> StationAsync(string plan = "starter")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), plan);
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio Mada", slug = "radio-mada" });
        response.Status(HttpStatusCode.Created);
        return (owner, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    /// <summary>A confirmed track, stored directly (uploads are covered in TrackTests).</summary>
    private async Task<Guid> TrackAsync(Guid stationId, string title, int seconds = 180)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var track = Track.StartUpload(stationId, $"{title}.mp3", 1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        track.Confirm(TrackFormat.Mp3, TimeSpan.FromSeconds(seconds), 1000, title, "Artist");
        db.Tracks.Add(track);
        await db.SaveChangesAsync(Token);
        return track.Id;
    }

    private static async Task<PlaylistResponse> PlaylistAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.Status(status);
        return (await response.Content.ReadFromJsonAsync<PlaylistResponse>(Browser.Json, Token))!;
    }

    private static async Task<AutoDjResponse> AutoDjAsync(HttpResponseMessage response)
    {
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AutoDjResponse>(Browser.Json, Token))!;
    }

    private static string Url(Guid station, string path = "") => $"/api/v1/stations/{station}{path}";

    [Fact]
    public async Task A_member_builds_reorders_renames_and_deletes_a_playlist()
    {
        var (owner, station) = await StationAsync();
        var dj = new Browser(factory) { TenantId = owner.TenantId };
        await Browser.AddMemberAsync(factory, owner.TenantId!.Value, await dj.SignUpAndLoginAsync(Browser.Unique("dj")), MembershipRole.Broadcaster);
        var (a, b, c) = (await TrackAsync(station.Id, "A", 60), await TrackAsync(station.Id, "B", 120), await TrackAsync(station.Id, "C", 30));

        var created = await PlaylistAsync(await dj.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Night rotation" }),
            HttpStatusCode.Created);
        Assert.Empty(created.Tracks);

        var filled = await PlaylistAsync(await dj.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{created.Id}/tracks"),
            new { trackIds = new[] { a, b, c, a } }));
        Assert.Equal(["A", "B", "C", "A"], filled.Tracks.Select(t => t.Title));
        Assert.Equal(270_000, filled.DurationMs);

        var reordered = await PlaylistAsync(await dj.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{created.Id}/tracks"),
            new { trackIds = new[] { c, b } }));
        Assert.Equal(["C", "B"], reordered.Tracks.Select(t => t.Title));

        var get = await dj.SendAsync(HttpMethod.Get, Url(station.Id, $"/playlists/{created.Id}"));
        var etag = get.Headers.ETag!.ToString();
        var renamed = await PlaylistAsync(await dj.SendIfMatchAsync(HttpMethod.Patch, Url(station.Id, $"/playlists/{created.Id}"), etag,
            new { name = "Late night", shuffle = true }));
        Assert.Equal(("Late night", true), (renamed.Name, renamed.Shuffle));
        // The ETag read before the rename is stale now.
        (await dj.SendIfMatchAsync(HttpMethod.Patch, Url(station.Id, $"/playlists/{created.Id}"), etag, new { name = "X" }))
            .Status(HttpStatusCode.PreconditionFailed);

        var list = await (await dj.SendAsync(HttpMethod.Get, Url(station.Id, "/playlists"))).Content
            .ReadFromJsonAsync<List<PlaylistSummaryResponse>>(Browser.Json, Token);
        Assert.Equal((2, 150_000L), (Assert.Single(list!).TrackCount, list![0].DurationMs));

        (await dj.SendAsync(HttpMethod.Delete, Url(station.Id, $"/playlists/{created.Id}"))).Status(HttpStatusCode.NoContent);
        (await dj.SendAsync(HttpMethod.Get, Url(station.Id, $"/playlists/{created.Id}"))).Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_station_s_own_tracks_can_be_added()
    {
        var (owner, station) = await StationAsync();
        var (_, other) = await StationAsync();
        var foreign = await TrackAsync(other.Id, "Foreign");
        var playlist = await PlaylistAsync(await owner.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Mix" }),
            HttpStatusCode.Created);

        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"), new { trackIds = new[] { foreign } }))
            .Status(HttpStatusCode.BadRequest);
        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"), new { trackIds = new[] { Guid.NewGuid() } }))
            .Status(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enabling_autodj_on_a_plan_without_it_is_refused_with_a_clear_message()
    {
        var (owner, station) = await StationAsync("free");
        var refused = await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true });

        refused.Status(HttpStatusCode.Forbidden);
        Assert.Contains("AutoDJ is not in the Free plan", await refused.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        var state = await AutoDjAsync(await owner.SendAsync(HttpMethod.Get, Url(station.Id, "/autodj")));
        Assert.Equal((false, false, false), (state.Enabled, state.Available, state.Active));
    }

    [Fact]
    public async Task Autodj_needs_a_non_empty_playlist_and_records_enable_and_disable()
    {
        var (owner, station) = await StationAsync();
        var playlist = await PlaylistAsync(await owner.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Fallback" }),
            HttpStatusCode.Created);

        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true })).Status(HttpStatusCode.Conflict);
        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true, defaultPlaylistId = playlist.Id }))
            .Status(HttpStatusCode.Conflict);
        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true, defaultPlaylistId = Guid.NewGuid() }))
            .Status(HttpStatusCode.BadRequest);

        await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"),
            new { trackIds = new[] { await TrackAsync(station.Id, "Jingle") } });
        var on = await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"),
            new { enabled = true, defaultPlaylistId = playlist.Id }));
        Assert.Equal((true, playlist.Id, true, false), (on.Active, on.DefaultPlaylistId!.Value, on.Available, on.Scheduling));

        var off = await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"),
            new { enabled = false, defaultPlaylistId = playlist.Id }));
        Assert.False(off.Active);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditEntries.IgnoreQueryFilters().Where(e => e.TargetId == station.Id.ToString())
            .OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync(Token);
        Assert.Equal(["station.created", "autodj.enabled", "autodj.disabled"], actions);
        var events = await db.OutboxMessages.Where(m => m.Type == nameof(AutoDjChanged)).Select(m => m.Payload).ToListAsync(Token);
        Assert.Equal(2, events.Count(p => p.Contains(station.Id.ToString(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_broadcaster_edits_playlists_but_cannot_switch_autodj()
    {
        var (owner, station) = await StationAsync();
        var dj = new Browser(factory) { TenantId = owner.TenantId };
        await Browser.AddMemberAsync(factory, owner.TenantId!.Value, await dj.SignUpAndLoginAsync(Browser.Unique("dj")), MembershipRole.Broadcaster);

        (await dj.SendAsync(HttpMethod.Get, Url(station.Id, "/autodj"))).Status(HttpStatusCode.OK);
        (await dj.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = false })).Status(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_playlist_autodj_plays_cannot_be_deleted_or_emptied_and_losing_its_last_track_turns_autodj_off()
    {
        var (owner, station) = await StationAsync();
        var track = await TrackAsync(station.Id, "Only one");
        var playlist = await PlaylistAsync(await owner.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Fallback" }),
            HttpStatusCode.Created);
        await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"), new { trackIds = new[] { track } });
        await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true, defaultPlaylistId = playlist.Id }));

        (await owner.SendAsync(HttpMethod.Delete, Url(station.Id, $"/playlists/{playlist.Id}"))).Status(HttpStatusCode.Conflict);
        (await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"), new { trackIds = Array.Empty<Guid>() }))
            .Status(HttpStatusCode.Conflict);

        (await owner.SendAsync(HttpMethod.Delete, Url(station.Id, $"/tracks/{track}"))).Status(HttpStatusCode.NoContent);

        var state = await AutoDjAsync(await owner.SendAsync(HttpMethod.Get, Url(station.Id, "/autodj")));
        Assert.Equal((false, playlist.Id), (state.Enabled, state.DefaultPlaylistId!.Value));
        Assert.Empty((await PlaylistAsync(await owner.SendAsync(HttpMethod.Get, Url(station.Id, $"/playlists/{playlist.Id}")))).Tracks);
    }

    [Fact]
    public async Task A_downgrade_keeps_the_choice_but_autodj_stops_being_active()
    {
        var (owner, station) = await StationAsync();
        var playlist = await PlaylistAsync(await owner.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Fallback" }),
            HttpStatusCode.Created);
        await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"),
            new { trackIds = new[] { await TrackAsync(station.Id, "Jingle") } });
        await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true, defaultPlaylistId = playlist.Id }));

        await Browser.SetPlanAsync(factory, owner.TenantId!.Value, "free");

        var state = await AutoDjAsync(await owner.SendAsync(HttpMethod.Get, Url(station.Id, "/autodj")));
        Assert.Equal((true, false, false), (state.Enabled, state.Available, state.Active));
        // Turning it off still works on any plan.
        Assert.False((await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = false }))).Enabled);
    }

    [Fact]
    public async Task Deleting_the_station_deletes_its_playlists_and_autodj_settings()
    {
        var (owner, station) = await StationAsync();
        var playlist = await PlaylistAsync(await owner.SendAsync(HttpMethod.Post, Url(station.Id, "/playlists"), new { name = "Fallback" }),
            HttpStatusCode.Created);
        await owner.SendAsync(HttpMethod.Put, Url(station.Id, $"/playlists/{playlist.Id}/tracks"),
            new { trackIds = new[] { await TrackAsync(station.Id, "Jingle") } });
        await AutoDjAsync(await owner.SendAsync(HttpMethod.Put, Url(station.Id, "/autodj"), new { enabled = true, defaultPlaylistId = playlist.Id }));

        (await owner.SendAsync(HttpMethod.Delete, Url(station.Id))).Status(HttpStatusCode.NoContent);
        while (await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token) > 0)
        {
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => p.StationId == station.Id, Token));
        Assert.False(await db.AutoDjSettings.IgnoreQueryFilters().AnyAsync(a => a.StationId == station.Id, Token));
        Assert.False(await db.Tracks.IgnoreQueryFilters().AnyAsync(t => t.StationId == station.Id, Token));
    }
}
