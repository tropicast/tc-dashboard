using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Media;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Media;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>The media library (#104): two-step upload checked on the stored object, quota, and cleanup.</summary>
public sealed class TrackTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private async Task<(Browser Owner, StationResponse Station)> StationAsync(string plan = "starter")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), plan);
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio Mada", slug = "radio-mada" });
        response.Status(HttpStatusCode.Created);
        return (owner, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    private static async Task<HttpResponseMessage> StartAsync(Browser browser, Guid station, string fileName, long size)
        => await browser.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station}/tracks/uploads", new { fileName, size });

    /// <summary>Starts the upload, PUTs the bytes (to the fake bucket) and confirms; returns the confirm response.</summary>
    private async Task<HttpResponseMessage> UploadAsync(Browser browser, Guid station, string fileName, byte[] bytes, object? confirm = null)
    {
        var start = await StartAsync(browser, station, fileName, bytes.Length);
        start.Status(HttpStatusCode.OK);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), bytes, upload.Headers["Content-Type"]);
        return await browser.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station}/tracks",
            confirm ?? new { uploadId = upload.TrackId });
    }

    private static async Task<TrackLibraryResponse> LibraryAsync(Browser browser, Guid station)
    {
        var response = await browser.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station}/tracks");
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TrackLibraryResponse>(Browser.Json, Token))!;
    }

    private bool Stored(Guid trackId) => factory.Storage.Keys.Any(k => k.EndsWith(trackId.ToString("N"), StringComparison.Ordinal));

    [Fact]
    public async Task A_member_uploads_lists_and_deletes_a_track_read_from_the_stored_file()
    {
        var (owner, station) = await StationAsync();
        var dj = new Browser(factory) { TenantId = owner.TenantId };
        await Browser.AddMemberAsync(factory, owner.TenantId!.Value, await dj.SignUpAndLoginAsync(Browser.Unique("dj")), MembershipRole.Broadcaster);
        var start = await StartAsync(dj, station.Id, "morning.mp3", 8_000);
        start.Status(HttpStatusCode.OK);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        Assert.Equal("audio/mpeg", upload.Headers["Content-Type"]);
        Assert.Equal("*", upload.Headers["If-None-Match"]);
        Assert.StartsWith($"stations/{station.Id:N}/tracks/", FakeObjectStorage.KeyOf(upload.UploadUrl), StringComparison.Ordinal);

        var bytes = Fixture("cbr.mp3");
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), bytes);
        var confirmed = await dj.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = upload.TrackId });
        confirmed.Status(HttpStatusCode.Created);
        var track = (await confirmed.Content.ReadFromJsonAsync<TrackResponse>(Browser.Json, Token))!;
        // From the file's tags and frames, not from the client.
        Assert.Equal(("Salegy Morning", "Radio Mada", TrackFormat.Mp3, (long)bytes.Length), (track.Title, track.Artist, track.Format, track.SizeBytes));
        Assert.InRange(track.DurationMs, 1950, 2100);

        var library = await LibraryAsync(dj, station.Id);
        Assert.Equal(track.Id, Assert.Single(library.Tracks).Id);
        Assert.Equal((bytes.Length, 1L << 30), (library.Storage.UsedBytes, library.Storage.QuotaBytes));

        (await dj.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}/tracks/{track.Id}")).Status(HttpStatusCode.NoContent);
        Assert.Empty((await LibraryAsync(dj, station.Id)).Tracks);
        Assert.False(Stored(track.Id));
    }

    [Fact]
    public async Task A_title_given_at_confirm_wins_and_a_file_without_tags_is_named_after_the_file()
    {
        var (owner, station) = await StationAsync();
        var named = await UploadAsync(owner, station.Id, "tone.opus", Fixture("tone.opus"));
        named.Status(HttpStatusCode.Created);
        Assert.Equal(("Opus Tone", TrackFormat.OggOpus), await TitleAndFormatAsync(named));

        // MPEG-1 frames with no tags at all.
        byte[] frame = [0xFF, 0xFB, 0x90, 0x00, .. new byte[413]];
        var untagged = await UploadAsync(owner, station.Id, "Station ID.mp3", [.. Enumerable.Repeat(frame, 100).SelectMany(f => f)]);
        untagged.Status(HttpStatusCode.Created);
        Assert.Equal(("Station ID", TrackFormat.Mp3), await TitleAndFormatAsync(untagged));

        var start = await StartAsync(owner, station.Id, "tone.flac", 1);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), Fixture("tone.flac"), "audio/flac");
        var given = await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks",
            new { uploadId = upload.TrackId, title = "Station jingle", artist = "Studio B" });
        given.Status(HttpStatusCode.Created);
        var track = (await given.Content.ReadFromJsonAsync<TrackResponse>(Browser.Json, Token))!;
        Assert.Equal(("Station jingle", "Studio B", TrackFormat.Flac), (track.Title, track.Artist, track.Format));

        static async Task<(string, TrackFormat)> TitleAndFormatAsync(HttpResponseMessage response)
        {
            var track = (await response.Content.ReadFromJsonAsync<TrackResponse>(Browser.Json, Token))!;
            return (track.Title, track.Format);
        }
    }

    [Fact]
    public async Task Unsupported_files_are_refused_before_and_after_upload_and_no_file_stays()
    {
        var (owner, station) = await StationAsync();
        (await StartAsync(owner, station.Id, "song.wav", 1000)).Status(HttpStatusCode.BadRequest);
        (await StartAsync(owner, station.Id, "huge.flac", 201L * 1024 * 1024)).Status(HttpStatusCode.BadRequest);

        // Named .mp3, but a web page.
        var fake = await UploadAsync(owner, station.Id, "song.mp3", "<html>not audio</html>"u8.ToArray());
        fake.Status(HttpStatusCode.UnprocessableEntity);
        Assert.Empty((await LibraryAsync(owner, station.Id)).Tracks);
        Assert.DoesNotContain(factory.Storage.Keys, k => k.Contains($"{station.Id:N}", StringComparison.Ordinal));
        // The pending row stays until it expires: its URL could still PUT, and the expiry cleanup deletes that file.
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tracks.IgnoreQueryFilters()
            .AllAsync(t => t.StationId != station.Id || t.Status == TrackStatus.Pending, Token));

        // An empty body stored under the URL (an aborted read in the browser): refused, not a crash.
        var empty = (await (await StartAsync(owner, station.Id, "empty.mp3", 8_000)).Content
            .ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        factory.Storage.Put(FakeObjectStorage.KeyOf(empty.UploadUrl), []);
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = empty.TrackId }))
            .Status(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Confirming_before_the_file_arrived_is_a_conflict_that_can_be_retried()
    {
        var (owner, station) = await StationAsync();
        var start = await StartAsync(owner, station.Id, "late.mp3", 8_000);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;

        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = upload.TrackId }))
            .Status(HttpStatusCode.Conflict);
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), Fixture("cbr.mp3"));
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = upload.TrackId }))
            .Status(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_quota_counts_pending_uploads_and_the_real_size_at_confirm()
    {
        var (owner, station) = await StationAsync();
        var quota = 1L << 30;
        // A pending upload holds its declared size.
        (await StartAsync(owner, station.Id, "big.flac", 150L * 1024 * 1024)).Status(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Fill the rest of the quota with stored tracks.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var filler = Track.StartUpload(station.Id, "filler.flac", 1, now, TimeSpan.FromMinutes(5));
            filler.Confirm(TrackFormat.Flac, TimeSpan.FromHours(1), quota - (150L * 1024 * 1024) - 20_000, "Filler", "");
            db.Tracks.Add(filler);
            await db.SaveChangesAsync(Token);
        }

        var over = await StartAsync(owner, station.Id, "one-more.mp3", 30_000);
        over.Status(HttpStatusCode.Forbidden);
        Assert.Contains("1 GB", await over.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        // Announced small, arrives larger than what is left (the bucket refuses it, as the URL is signed for the
        // declared size; the confirm checks again): refused at confirm, and deleted.
        var start = await StartAsync(owner, station.Id, "liar.mp3", 1_000);
        start.Status(HttpStatusCode.OK);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        byte[] frame = [0xFF, 0xFB, 0x90, 0x00, .. new byte[413]];
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), [.. Enumerable.Repeat(frame, 60).SelectMany(f => f)]);
        var confirm = await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = upload.TrackId });
        confirm.Status(HttpStatusCode.Forbidden);
        Assert.False(Stored(upload.TrackId));
    }

    [Fact]
    public async Task A_plan_without_autodj_cannot_upload()
    {
        var (owner, station) = await StationAsync("free");
        var refused = await StartAsync(owner, station.Id, "song.mp3", 8_000);
        refused.Status(HttpStatusCode.Forbidden);
        Assert.Contains("AutoDJ is not in the Free plan", await refused.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(0, (await LibraryAsync(owner, station.Id)).Storage.QuotaBytes);
    }

    [Fact]
    public async Task Other_tenants_get_404_for_every_track_endpoint()
    {
        var (owner, station) = await StationAsync();
        var created = await UploadAsync(owner, station.Id, "cbr.mp3", Fixture("cbr.mp3"));
        var track = (await created.Content.ReadFromJsonAsync<TrackResponse>(Browser.Json, Token))!;
        var (other, _) = await StationAsync();

        (await other.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/tracks")).Status(HttpStatusCode.NotFound);
        (await StartAsync(other, station.Id, "x.mp3", 10)).Status(HttpStatusCode.NotFound);
        (await other.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = track.Id })).Status(HttpStatusCode.NotFound);
        (await other.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}/tracks/{track.Id}")).Status(HttpStatusCode.NotFound);
        Assert.True(Stored(track.Id));
    }

    [Fact]
    public async Task Without_a_bucket_uploads_answer_503()
    {
        var (owner, station) = await StationAsync();
        var created = await UploadAsync(owner, station.Id, "cbr.mp3", Fixture("cbr.mp3"));
        var track = (await created.Content.ReadFromJsonAsync<TrackResponse>(Browser.Json, Token))!;
        factory.Storage.IsConfigured = false;
        try
        {
            (await StartAsync(owner, station.Id, "song.mp3", 8_000)).Status(HttpStatusCode.ServiceUnavailable);
            // The row would go without its file, which nothing could delete later.
            (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}/tracks/{track.Id}"))
                .Status(HttpStatusCode.ServiceUnavailable);
            Assert.Single((await LibraryAsync(owner, station.Id)).Tracks);
        }
        finally
        {
            factory.Storage.IsConfigured = true;
        }
    }

    [Fact]
    public async Task Deleting_the_station_deletes_its_files_and_tracks_and_the_tenant_shows_storage_use()
    {
        var (owner, station) = await StationAsync();
        var first = await UploadAsync(owner, station.Id, "cbr.mp3", Fixture("cbr.mp3"));
        first.Status(HttpStatusCode.Created);
        (await UploadAsync(owner, station.Id, "tone.ogg", Fixture("tone.ogg"))).Status(HttpStatusCode.Created);
        var pending = (await (await StartAsync(owner, station.Id, "slow.flac", 8_000)).Content
            .ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        var tenant = await (await owner.SendAsync(HttpMethod.Get, "/api/v1/tenants/current")).Content
            .ReadFromJsonAsync<System.Text.Json.JsonElement>(Token);
        Assert.Equal(Fixture("cbr.mp3").Length + Fixture("tone.ogg").Length + 8_000, tenant.GetProperty("storageUsedBytes").GetInt64());
        Assert.True(tenant.GetProperty("plan").GetProperty("autoDj").GetBoolean());

        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NoContent);
        await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token);

        Assert.DoesNotContain(factory.Storage.Keys, k => k.StartsWith(Track.StationPrefix(station.Id), StringComparison.Ordinal));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The pending upload stays: a PUT still running would land after the prefix was cleared.
        Assert.Equal([pending.TrackId], await db.Tracks.IgnoreQueryFilters().Where(t => t.StationId == station.Id)
            .Select(t => t.Id).ToListAsync(Token));
        factory.Storage.Put(FakeObjectStorage.KeyOf(pending.UploadUrl), Fixture("cbr.mp3"));

        await PendingUploadCleanup.DeleteExpiredAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            DateTimeOffset.UtcNow + TrackRules.PendingLifetime + TimeSpan.FromMinutes(1), Token);

        Assert.False(Stored(pending.TrackId));
        Assert.False(await db.Tracks.IgnoreQueryFilters().AnyAsync(t => t.StationId == station.Id, Token));
    }

    [Fact]
    public async Task Expired_uploads_are_deleted_with_their_file_well_after_their_url_expired()
    {
        var (owner, station) = await StationAsync();
        var start = await StartAsync(owner, station.Id, "abandoned.mp3", 8_000);
        var upload = (await start.Content.ReadFromJsonAsync<TrackUploadResponse>(Browser.Json, Token))!;
        factory.Storage.Put(FakeObjectStorage.KeyOf(upload.UploadUrl), Fixture("cbr.mp3"));
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();

        // A PUT started just before the URL expired may still be running: the upload stays.
        await PendingUploadCleanup.DeleteExpiredAsync(scopes, upload.ExpiresAt.AddMinutes(10), Token);
        Assert.True(Stored(upload.TrackId));

        await PendingUploadCleanup.DeleteExpiredAsync(scopes, DateTimeOffset.UtcNow + TrackRules.PendingLifetime + TimeSpan.FromMinutes(1), Token);

        Assert.False(Stored(upload.TrackId));
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/tracks", new { uploadId = upload.TrackId }))
            .Status(HttpStatusCode.NotFound);
    }
}
