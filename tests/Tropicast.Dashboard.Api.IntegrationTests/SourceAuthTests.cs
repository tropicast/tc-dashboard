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
        Assert.True(response.Headers.TryGetValues("icecast-auth-user", out var values) && values.Single() == "1",
            response.Headers.TryGetValues("icecast-auth-message", out var reasons) ? reasons.Single() : "no reason");
    }

    private static string Denied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("icecast-auth-user"));
        return response.Headers.GetValues("icecast-auth-message").Single();
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
}
