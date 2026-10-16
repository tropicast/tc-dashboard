using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Contract tests: what Icecast sends and must get back (tc-streaming docs/source-auth.md).</summary>
public sealed class SourceAuthTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Icecast's view: the internal port, node credentials, a form body.</summary>
    private HttpClient Icecast(string? username = SourceAuthNode.Username, string? password = SourceAuthNode.Password)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
        if (username is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        }
        return client;
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient icecast, string mount, string user, string pass,
        params (string Key, string Value)[] headers)
    {
        var fields = new Dictionary<string, string>
        {
            ["action"] = "stream_auth", ["mount"] = mount, ["user"] = user, ["pass"] = pass, ["ip"] = "172.18.0.3",
            ["agent"] = "Lavf/61.7.100", ["client"] = "17", ["server"] = "listen.tropicastradio.com", ["port"] = "8000",
            ["header.content-type"] = mount.EndsWith(".opus", StringComparison.Ordinal) ? "audio/ogg" : "audio/mpeg",
        };
        foreach (var (key, value) in headers)
        {
            fields[key] = value;
        }
        return icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(fields), Token);
    }

    private static void Allowed(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("x-icecast-auth-result", out var values) && values.Single() == "ok",
            response.Headers.TryGetValues("x-icecast-auth-message", out var reasons) ? reasons.Single() : "no reason");
        // Old style too, for nodes whose Icecast is not set to the new headers yet.
        Assert.Equal("1", response.Headers.GetValues("icecast-auth-user").Single());
    }

    private static string Denied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("x-icecast-auth-result"));
        Assert.False(response.Headers.Contains("icecast-auth-user"));
        var reason = response.Headers.GetValues("x-icecast-auth-message").Single();
        Assert.Equal(reason, response.Headers.GetValues("icecast-auth-message").Single());
        return reason;
    }

    private async Task<(Browser Owner, StationResponse Station, string Secret, Guid CredentialId)> StationAsync(string plan = "free")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), plan);
        var station = (await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" }))
            .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        var issued = (await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "Studio PC" }))
            .Content.ReadFromJsonAsync<IssuedCredentialResponse>(Browser.Json, Token))!;
        Assert.Equal(station.PublicId, issued.Username);
        Assert.Equal($"/stations/{station.PublicId}/live.mp3", issued.IngestUrl.AbsolutePath);
        Assert.Equal(Uri.UriSchemeHttps, issued.IngestUrl.Scheme);
        return (owner, station, issued.Secret, issued.Credential.Id);
    }

    [Fact]
    public async Task Requests_without_the_node_credentials_get_401()
    {
        var (_, station, secret, _) = await StationAsync();
        var mount = $"/stations/{station.PublicId}/live.mp3";
        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(Icecast(null), mount, station.PublicId, secret)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(Icecast(password: "wrong"), mount, station.PublicId, secret)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(Icecast("other"), mount, station.PublicId, secret)).StatusCode);
    }

    [Fact]
    public async Task A_station_publishes_with_its_credential_and_the_use_is_recorded()
    {
        var (_, station, secret, credentialId) = await StationAsync();
        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret,
            ("header.ice-bitrate", "64"), ("header.ice-audio-info", "bitrate=64;samplerate=44100;channels=2")));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credential = await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == credentialId, Token);
        Assert.NotNull(credential.LastUsedAt);
        var session = await db.LiveSessions.IgnoreQueryFilters().SingleAsync(s => s.StationId == station.Id, Token);
        Assert.Equal((AudioFormat.Mp3, (DateTimeOffset?)null), (session.Format, session.EndedAt));

        // A reconnect after a node restart ends the stale session and opens a new one.
        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret));
        var sessions = await db.LiveSessions.IgnoreQueryFilters().AsNoTracking().Where(s => s.StationId == station.Id).ToListAsync(Token);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.EndedAt is null);
    }

    [Fact]
    public async Task Station_A_credential_cannot_publish_to_station_B()
    {
        var (_, a, secretA, _) = await StationAsync();
        var (_, b, _, _) = await StationAsync();
        Denied(await AskAsync(Icecast(), $"/stations/{b.PublicId}/live.mp3", a.PublicId, secretA));
        Denied(await AskAsync(Icecast(), $"/stations/{b.PublicId}/live.mp3", b.PublicId, secretA));
    }

    [Fact]
    public async Task Wrong_passwords_other_mounts_and_users_are_denied_without_echoing_the_password()
    {
        var (_, station, secret, _) = await StationAsync();
        var reason = Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, "a-wrong-password"));
        Assert.DoesNotContain("a-wrong-password", reason, StringComparison.Ordinal);
        Denied(await AskAsync(Icecast(), "/other.mp3", station.PublicId, secret));
        Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", "source", secret));
        Assert.DoesNotContain(factory.Logs.Lines, l => l.Contains(secret, StringComparison.Ordinal) || l.Contains("a-wrong-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_revoked_credential_is_rejected_on_the_next_connect()
    {
        var (owner, station, secret, credentialId) = await StationAsync();
        var mount = $"/stations/{station.PublicId}/live.mp3";
        Allowed(await AskAsync(Icecast(), mount, station.PublicId, secret));
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}/credentials/{credentialId}")).Status(HttpStatusCode.NoContent);
        Denied(await AskAsync(Icecast(), mount, station.PublicId, secret));
    }

    [Fact]
    public async Task A_deleted_station_cannot_broadcast()
    {
        var (owner, station, secret, _) = await StationAsync();
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NoContent);
        Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret));
    }

    [Fact]
    public async Task The_plan_limits_bitrate_and_format()
    {
        var (_, station, secret, _) = await StationAsync("free");
        var mp3 = $"/stations/{station.PublicId}/live.mp3";
        Assert.Contains("above", Denied(await AskAsync(Icecast(), mp3, station.PublicId, secret, ("header.ice-bitrate", "128"))), StringComparison.Ordinal);
        Denied(await AskAsync(Icecast(), mp3, station.PublicId, secret, ("header.ice-audio-info", "bitrate=128;samplerate=44100")));
        Allowed(await AskAsync(Icecast(), mp3, station.PublicId, secret));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE plans SET formats = 'Mp3' WHERE id = 'free'", Token);
        }
        try
        {
            Assert.Contains("format opus", Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.opus", station.PublicId, secret)),
                StringComparison.Ordinal);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE plans SET formats = 'Mp3, Opus' WHERE id = 'free'", Token);
        }
    }

    [Fact]
    public async Task The_endpoint_is_not_served_on_the_public_port()
    {
        var (_, station, secret, _) = await StationAsync();
        var publicClient = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8080") });
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{SourceAuthNode.Password}")));
        var response = await AskAsync(publicClient, $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("icecast-auth-user"));
    }

    [Fact]
    public async Task It_answers_quickly()
    {
        var (_, station, secret, _) = await StationAsync();
        var mount = $"/stations/{station.PublicId}/live.mp3";
        await AskAsync(Icecast(), mount, station.PublicId, secret);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            Allowed(await AskAsync(Icecast(), mount, station.PublicId, secret));
        }
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4), $"20 decisions took {timer.Elapsed}");
    }

    [Fact]
    public async Task A_spoofed_host_on_the_public_listener_does_not_reach_it()
    {
        var (_, station, secret, _) = await StationAsync();
        var client = Icecast();
        client.DefaultRequestHeaders.Add("X-Test-Local-Port", "8080");
        var response = await AskAsync(client, $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("icecast-auth-user"));
    }

    [Fact]
    public async Task Malformed_or_oversized_forms_are_denied_not_errors()
    {
        var (_, station, secret, _) = await StationAsync();
        using var huge = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mount"] = $"/stations/{station.PublicId}/live.mp3", ["user"] = station.PublicId, ["pass"] = secret,
            ["padding"] = new string('x', 10_000),
        });
        Denied(await Icecast().PostAsync("/internal/icecast/source-auth", huge, Token));
        using var broken = new StringContent("mount=%ZZ&user=", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await Icecast().PostAsync("/internal/icecast/source-auth", broken, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("icecast-auth-user"));
    }

    [Fact]
    public async Task An_out_of_range_bitrate_cannot_bypass_the_plan()
    {
        var (_, station, secret, _) = await StationAsync();
        var mount = $"/stations/{station.PublicId}/live.mp3";
        Denied(await AskAsync(Icecast(), mount, station.PublicId, secret, ("header.ice-bitrate", "99999999999999999999999")));
        Denied(await AskAsync(Icecast(), mount, station.PublicId, secret, ("header.ice-audio-info", "bitrate=99999999999999999999")));
        Denied(await AskAsync(Icecast(), mount, station.PublicId, secret, ("header.ice-bitrate", "fast")));
    }

    /// <summary>A Starter station with AutoDJ on (one track in its playlist); returns its AutoDJ secret as the worker gets it.</summary>
    private async Task<(Browser Owner, StationResponse Station, string DeviceSecret, string AutoDjSecret)> AutoDjStationAsync()
    {
        var (owner, station, deviceSecret, _) = await StationAsync("starter");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var track = Domain.Media.Track.StartUpload(station.Id, "jingle.mp3", 1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
            track.Confirm(Domain.Media.TrackFormat.Mp3, TimeSpan.FromSeconds(30), 1000, "Jingle", "");
            var playlist = Domain.Media.Playlist.Create(station.Id, "Fallback", false, DateTimeOffset.UtcNow);
            playlist.SetTracks([track.Id], DateTimeOffset.UtcNow);
            db.Tracks.Add(track);
            db.Playlists.Add(playlist);
            await db.SaveChangesAsync(Token);
            (await owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Id}/autodj", new { enabled = true, defaultPlaylistId = playlist.Id }))
                .Status(HttpStatusCode.OK);
        }
        return (owner, station, deviceSecret, await AutoDjSecretAsync(station.Id));
    }

    private async Task<string> AutoDjSecretAsync(Guid stationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BroadcastCredentials.IgnoreQueryFilters()
            .SingleAsync(c => c.StationId == stationId && c.Purpose == Domain.Stations.CredentialPurpose.AutoDj && c.RevokedAt == null, Token);
        return Media.AutoDjCredentials.Reveal(scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
            credential)!;
    }

    /// <summary>An ICY metadata update as Icecast forwards it (tc-streaming#72): the request URI as mount, no headers.</summary>
    private static Task<HttpResponseMessage> MetadataAsync(HttpClient icecast, string uri, string user, string pass)
        => icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "metadata_auth", ["mount"] = uri, ["user"] = user, ["pass"] = pass, ["ip"] = "172.18.0.3",
            ["agent"] = "Liquidsoap/2.4.6", ["client"] = "18", ["server"] = "listen.tropicastradio.com", ["port"] = "8000",
        }), Token);

    [Fact]
    public async Task A_source_updates_its_own_title_with_its_credential_and_nothing_is_recorded()
    {
        var (_, station, secret, credentialId) = await StationAsync();
        var (_, other, otherSecret, _) = await StationAsync();
        string Update(string publicId, string extra = "") =>
            $"/admin/metadata?mode=updinfo&mount=%2Fstations%2F{publicId}%2Flive.mp3&song=Artist%20-%20Title{extra}";

        Allowed(await MetadataAsync(Icecast(), Update(station.PublicId), station.PublicId, secret));
        // Not another station's title, not with a wrong password, and only one mount per update.
        Denied(await MetadataAsync(Icecast(), Update(other.PublicId), station.PublicId, secret));
        Denied(await MetadataAsync(Icecast(), Update(station.PublicId), other.PublicId, otherSecret));
        Denied(await MetadataAsync(Icecast(), Update(station.PublicId), station.PublicId, "wrong"));
        Denied(await MetadataAsync(Icecast(), Update(station.PublicId, $"&mount=%2Fstations%2F{station.PublicId}%2Flive.opus"),
            station.PublicId, secret));
        // The URI must be a metadata update: a plain mount is not one.
        Denied(await MetadataAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, secret));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.LiveSessions.IgnoreQueryFilters().AnyAsync(s => s.StationId == station.Id, Token));
        Assert.Null((await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == credentialId, Token)).LastUsedAt);
    }

    [Fact]
    public async Task AutoDJ_updates_its_title_with_the_autodj_credential_only()
    {
        var (_, station, deviceSecret, autoDjSecret) = await AutoDjStationAsync();
        var uri = $"/admin/metadata?mode=updinfo&mount=/stations/{station.PublicId}/autodj.mp3&charset=UTF-8&song=Tropicast%20-%20Track";

        Allowed(await MetadataAsync(Icecast(), uri, station.PublicId, autoDjSecret));
        Denied(await MetadataAsync(Icecast(), uri, station.PublicId, deviceSecret));
    }

    [Fact]
    public async Task The_autodj_credential_publishes_only_to_its_own_autodj_mounts()
    {
        var (_, station, deviceSecret, autoDjSecret) = await AutoDjStationAsync();
        var (_, other, _, otherAutoDj) = await AutoDjStationAsync();

        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/autodj.mp3", station.PublicId, autoDjSecret,
            ("header.ice-audio-info", "bitrate=128;samplerate=44100;channels=2")));
        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/autodj.opus", station.PublicId, autoDjSecret));
        // Not on the live mount, not on another station, and device credentials never on AutoDJ mounts.
        Assert.Equal("wrong or revoked credential", Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, autoDjSecret)));
        Denied(await AskAsync(Icecast(), $"/stations/{other.PublicId}/autodj.mp3", other.PublicId, autoDjSecret));
        Denied(await AskAsync(Icecast(), $"/stations/{other.PublicId}/autodj.mp3", station.PublicId, autoDjSecret));
        Assert.Equal("wrong or revoked credential", Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/autodj.mp3", station.PublicId, deviceSecret)));
        Denied(await AskAsync(Icecast(), $"/stations/{station.PublicId}/autodj.mp3", station.PublicId, otherAutoDj));
        // AutoDJ is no live session.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.LiveSessions.IgnoreQueryFilters().AnyAsync(s => s.StationId == station.Id, Token));
    }

    [Fact]
    public async Task Turning_autodj_off_revokes_its_credential_and_turning_it_on_again_rotates_it()
    {
        var (owner, station, _, oldSecret) = await AutoDjStationAsync();
        var mount = $"/stations/{station.PublicId}/autodj.mp3";
        var settings = await (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/autodj")).Content
            .ReadFromJsonAsync<System.Text.Json.JsonElement>(Token);
        var playlist = settings.GetProperty("defaultPlaylistId").GetGuid();

        (await owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Id}/autodj", new { enabled = false })).Status(HttpStatusCode.OK);
        Assert.Equal("AutoDJ is off for this station", Denied(await AskAsync(Icecast(), mount, station.PublicId, oldSecret)));

        (await owner.SendAsync(HttpMethod.Put, $"/api/v1/stations/{station.Id}/autodj", new { enabled = true, defaultPlaylistId = playlist }))
            .Status(HttpStatusCode.OK);
        var newSecret = await AutoDjSecretAsync(station.Id);
        Assert.NotEqual(oldSecret, newSecret);
        Assert.Equal("wrong or revoked credential", Denied(await AskAsync(Icecast(), mount, station.PublicId, oldSecret)));
        Allowed(await AskAsync(Icecast(), mount, station.PublicId, newSecret));
        // System-managed: not listed with the devices.
        var devices = await (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/credentials")).Content
            .ReadFromJsonAsync<List<System.Text.Json.JsonElement>>(Token);
        Assert.Equal(["Studio PC"], devices!.Select(d => d.GetProperty("deviceLabel").GetString()));
    }

    [Fact]
    public async Task After_a_downgrade_or_station_deletion_the_autodj_credential_is_refused()
    {
        var (owner, station, _, secret) = await AutoDjStationAsync();
        var mount = $"/stations/{station.PublicId}/autodj.mp3";

        await Browser.SetPlanAsync(factory, owner.TenantId!.Value, "free");
        Assert.Equal("AutoDJ is off for this station", Denied(await AskAsync(Icecast(), mount, station.PublicId, secret)));
        await Browser.SetPlanAsync(factory, owner.TenantId!.Value, "starter");
        Allowed(await AskAsync(Icecast(), mount, station.PublicId, secret));

        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NoContent);
        Denied(await AskAsync(Icecast(), mount, station.PublicId, secret));
        while (await factory.Services.GetRequiredService<Infrastructure.Outbox.OutboxDispatcher>().DispatchBatchAsync(Token) > 0)
        {
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BroadcastCredentials.IgnoreQueryFilters()
            .SingleAsync(c => c.StationId == station.Id && c.Purpose == Domain.Stations.CredentialPurpose.AutoDj, Token);
        Assert.Equal((true, (string?)null), (credential.RevokedAt is not null, credential.ProtectedSecret));
    }

    [Fact]
    public async Task A_live_connect_pins_the_encoding_autodj_uses()
    {
        var (owner, station, deviceSecret, _) = await AutoDjStationAsync();
        static async Task<List<System.Text.Json.JsonElement>> EncodingAsync(Browser owner, Guid station)
            => [.. (await (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station}/autodj")).Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>(Token)).GetProperty("encoding").EnumerateArray()];

        var defaults = await EncodingAsync(owner, station.Id);
        Assert.Equal(["mp3:128:44100:False", "opus:64:48000:False"], defaults.Select(Describe));

        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.mp3", station.PublicId, deviceSecret,
            ("header.ice-bitrate", "96"), ("header.ice-audio-info", "bitrate=96;samplerate=48000;channels=1")));
        // Nothing declared: the pinned Opus profile stays the default.
        Allowed(await AskAsync(Icecast(), $"/stations/{station.PublicId}/live.opus", station.PublicId, deviceSecret));

        Assert.Equal(["mp3:96:48000:True", "opus:64:48000:False"], (await EncodingAsync(owner, station.Id)).Select(Describe));

        static string Describe(System.Text.Json.JsonElement e) => $"{e.GetProperty("format").GetString()}:{e.GetProperty("bitrateKbps").GetInt32()}:"
            + $"{e.GetProperty("sampleRate").GetInt32()}:{e.GetProperty("pinned").GetBoolean()}";
    }
}
