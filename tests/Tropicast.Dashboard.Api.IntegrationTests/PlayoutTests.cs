using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Playout;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>The stations.json flag and the playout worker's endpoint (#107).</summary>
public sealed class PlayoutTests(DashboardFactory factory, PostgresContainer postgres) : IClassFixture<DashboardFactory>
{
    private readonly DashboardFactory _factory = factory;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] TrackTitles = ["Jingle", "Morning song"];

    private HttpClient Node(string? password = SourceAuthNode.Password, int port = 8081)
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri($"http://localhost:{port}") });
        if (password is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{password}")));
        }
        return client;
    }

    private sealed record AutoDjStation(Browser Owner, StationResponse Station, Guid PlaylistId, Guid[] Tracks);

    private async Task<AutoDjStation> StationAsync(DashboardFactory? factory = null, bool enable = true)
    {
        factory ??= _factory;
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), "starter");
        var station = (await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" }))
            .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracks = TrackTitles.Select(title =>
        {
            var track = Track.StartUpload(station.Id, $"{title}.mp3", 1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
            track.Confirm(TrackFormat.Mp3, TimeSpan.FromSeconds(60), 1000, title, "Artist");
            return track;
        }).ToList();
        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync(Token);
        var playlist = (await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/playlists", new { name = "Fallback" }))
            .Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
        (await owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Id}/playlists/{playlist}/tracks",
            new { trackIds = new[] { tracks[1].Id, tracks[0].Id } })).Status(HttpStatusCode.OK);
        if (enable)
        {
            (await owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Id}/autodj", new { enabled = true, defaultPlaylistId = playlist }))
                .Status(HttpStatusCode.OK);
        }
        return new AutoDjStation(owner, station, playlist, [.. tracks.Select(t => t.Id)]);
    }

    private async Task<(PlayoutResponse Body, string ETag)> PlayoutAsync()
    {
        var response = await Node().GetAsync(PlayoutEndpoint.Path, Token);
        response.Status(HttpStatusCode.OK);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        return ((await response.Content.ReadFromJsonAsync<PlayoutResponse>(Browser.Json, Token))!, response.Headers.ETag!.ToString());
    }

    private async Task<string> ReconciledAsync()
    {
        while (await _factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token) > 0)
        {
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Reconciler>().ReconcileAsync(Token);
        return _factory.Node.Applied[^1];
    }

    private static bool AutoDjIn(string stationsJson, string publicId)
        => JsonDocument.Parse(stationsJson).RootElement.GetProperty("stations").GetProperty(publicId).TryGetProperty("autodj", out var flag)
            && flag.GetBoolean();

    [Fact]
    public async Task Only_the_node_on_the_internal_port_reads_it()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Node(null).GetAsync(PlayoutEndpoint.Path, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Node("wrong").GetAsync(PlayoutEndpoint.Path, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Node(port: 8080).GetAsync(PlayoutEndpoint.Path, Token)).StatusCode);
    }

    [Fact]
    public async Task An_autodj_station_comes_with_its_encoding_credential_and_tracks_in_play_order()
    {
        var station = await StationAsync();
        var (body, _) = await PlayoutAsync();

        var entry = Assert.Single(body.Stations, s => s.PublicId == station.Station.PublicId);
        Assert.Equal(["/stations/" + station.Station.PublicId + "/autodj.mp3:128:44100", "/stations/" + station.Station.PublicId + "/autodj.opus:64:48000"],
            entry.Outputs.Select(o => $"{o.Mount}:{o.BitrateKbps}:{o.SampleRate}"));
        Assert.Equal(station.PlaylistId, entry.Playlist.Id);
        Assert.Equal(["Morning song", "Jingle"], entry.Playlist.Tracks.Select(t => t.Title));
        Assert.All(entry.Playlist.Tracks, t => Assert.StartsWith($"https://bucket.test/stations/{station.Station.Id:N}/tracks/", t.Url.ToString(), StringComparison.Ordinal));
        Assert.True(body.UrlsExpireAt > DateTimeOffset.UtcNow.AddMinutes(10));

        // The credential it hands out is the one source auth accepts on the AutoDJ mount, and only there.
        var icecast = Node();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "stream_auth", ["mount"] = $"/stations/{entry.PublicId}/autodj.mp3", ["user"] = entry.Username, ["pass"] = entry.Password,
        });
        var auth = await icecast.PostAsync("/internal/icecast/source-auth", form, Token);
        Assert.Equal("1", auth.Headers.GetValues("icecast-auth-user").Single());
    }

    [Fact]
    public async Task The_version_changes_with_the_playlist_and_304_answers_an_unchanged_poll()
    {
        var station = await StationAsync();
        var (first, etag) = await PlayoutAsync();
        var again = await PlayoutAsync();
        Assert.Equal(first.Version, again.Body.Version);

        var poll = Node();
        poll.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await poll.GetAsync(PlayoutEndpoint.Path, Token)).StatusCode);

        (await station.Owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Station.Id}/playlists/{station.PlaylistId}/tracks",
            new { trackIds = new[] { station.Tracks[0] } })).Status(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await poll.GetAsync(PlayoutEndpoint.Path, Token)).StatusCode);
        Assert.NotEqual(first.Version, (await PlayoutAsync()).Body.Version);
    }

    [Fact]
    public async Task Enabling_puts_the_station_in_stations_json_and_the_endpoint_and_disabling_removes_it_from_both()
    {
        var station = await StationAsync(enable: false);
        Assert.False(AutoDjIn(await ReconciledAsync(), station.Station.PublicId));
        Assert.DoesNotContain((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId);

        (await station.Owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Station.Id}/autodj",
            new { enabled = true, defaultPlaylistId = station.PlaylistId })).Status(HttpStatusCode.OK);
        Assert.True(AutoDjIn(await ReconciledAsync(), station.Station.PublicId));
        Assert.Contains((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId);

        (await station.Owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Station.Id}/autodj", new { enabled = false }))
            .Status(HttpStatusCode.OK);
        Assert.False(AutoDjIn(await ReconciledAsync(), station.Station.PublicId));
        Assert.DoesNotContain((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId);
    }

    [Fact]
    public async Task A_downgrade_takes_the_station_out_of_both()
    {
        var station = await StationAsync();
        Assert.True(AutoDjIn(await ReconciledAsync(), station.Station.PublicId));

        await Browser.SetPlanAsync(_factory, station.Owner.TenantId!.Value, "free");

        Assert.False(AutoDjIn(await ReconciledAsync(), station.Station.PublicId));
        Assert.DoesNotContain((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId);
    }

    [Fact]
    public async Task A_credential_that_cannot_be_decrypted_is_rotated()
    {
        var station = await StationAsync();
        var before = Assert.Single((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId).Password;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(
                $"UPDATE broadcast_credentials SET protected_secret = 'not-a-protected-payload' WHERE station_id = {station.Station.Id} AND purpose = 'AutoDj'",
                Token);
        }

        var after = Assert.Single((await PlayoutAsync()).Body.Stations, s => s.PublicId == station.Station.PublicId).Password;

        Assert.NotEqual(before, after);
        Assert.Equal(43, after.Length);
    }

    [Fact]
    public async Task Autodj_mounts_count_toward_the_node_s_sources()
    {
        // Two sources: the station's live MP3 and Opus mounts fill the node; AutoDJ would need two more.
        await using var small = new DashboardFactory(postgres) { Settings = new Dictionary<string, string> { ["Streaming:MaxSources"] = "2" } };
        await small.InitializeAsync();
        var station = await StationAsync(small, enable: false);

        var refused = await station.Owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Station.Id}/autodj",
            new { enabled = true, defaultPlaylistId = station.PlaylistId });

        refused.Status(HttpStatusCode.ServiceUnavailable);
        Assert.Contains(small.Logs.Lines, l => l.StartsWith("Critical", StringComparison.Ordinal) && l.Contains("is full", StringComparison.Ordinal));
    }
}
