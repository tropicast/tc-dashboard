using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>The desktop app's path: device sign-in, pick a station, get its broadcast target (docs/desktop-api.md).</summary>
public sealed class DesktopTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<DeviceCodeResponse> StartAsync(HttpClient desktop, string deviceName = "Studio PC")
    {
        var response = await desktop.PostAsJsonAsync("/api/v1/auth/device/code", new { deviceName }, Token);
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(Token))!;
    }

    private static Task<HttpResponseMessage> PollAsync(HttpClient desktop, string deviceCode)
        => desktop.PostAsJsonAsync("/api/v1/auth/device/token", new { deviceCode }, Token);

    private static async Task<string> PollErrorAsync(HttpClient desktop, string deviceCode)
    {
        var response = await PollAsync(desktop, deviceCode);
        response.Status(HttpStatusCode.BadRequest);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("error").GetString()!;
    }

    /// <summary>Moves the last poll (and optionally the expiry) into the past instead of waiting.</summary>
    private async Task RewindAsync(string deviceCode, bool expire = false)
    {
        Assert.True(DeviceAuthorization.TryGetId(deviceCode, out var id));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var past = DateTimeOffset.UtcNow - DeviceAuthorization.PollInterval;
        await db.DeviceAuthorizations.Where(r => r.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.LastPolledAt, past)
            .SetProperty(r => r.ExpiresAt, r => expire ? past : r.ExpiresAt), Token);
    }

    /// <summary>The whole device sign-in: the app starts it, the signed-in user approves the code, the app polls.</summary>
    private static async Task<TokenResponse> SignInDeviceAsync(Browser user, HttpClient desktop, string deviceName = "Studio PC")
    {
        var code = await StartAsync(desktop, deviceName);
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = code.UserCode })).Status(HttpStatusCode.NoContent);
        var response = await PollAsync(desktop, code.DeviceCode);
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient desktop, HttpMethod method, string url, string accessToken,
        Guid? tenantId = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = Browser.Bearer(accessToken);
        if (tenantId is { } tenant)
        {
            request.Headers.Add("X-Tenant-Id", tenant.ToString());
        }
        return await desktop.SendAsync(request, Token);
    }

    private async Task<(Browser Owner, Guid TenantId, StationResponse Station)> StationAsync(string name = "Radio")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name, slug = "radio" });
        response.Status(HttpStatusCode.Created);
        return (owner, tenantId, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    private static async Task<BroadcastTargetResponse> TargetAsync(HttpClient desktop, TokenResponse tokens, Guid tenantId, Guid stationId)
    {
        var response = await SendAsync(desktop, HttpMethod.Post, $"/api/v1/stations/{stationId}/broadcast-target", tokens.AccessToken, tenantId);
        response.Status(HttpStatusCode.OK);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<BroadcastTargetResponse>(Browser.Json, Token))!;
    }

    [Fact]
    public async Task A_device_signs_in_once_the_user_approves_its_code()
    {
        var user = new Browser(factory);
        await user.SignUpAndLoginAsync(Browser.Unique("dj"));
        var desktop = factory.CreateClient();

        var code = await StartAsync(desktop);
        Assert.Matches("^[B-Z]{4}-[B-Z]{4}$", code.UserCode);
        Assert.Equal(new Uri("https://app.test/device"), code.VerificationUri);
        Assert.Equal(new Uri($"https://app.test/device?code={code.UserCode}"), code.VerificationUriComplete);
        Assert.Equal(600, code.ExpiresIn);
        Assert.Equal(5, code.Interval);

        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Pending, await PollErrorAsync(desktop, code.DeviceCode));
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.SlowDown, await PollErrorAsync(desktop, code.DeviceCode));

        // The user sees which device asks, typing the code in any case and without the dash.
        var typed = code.UserCode.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        var shown = await user.Http.GetFromJsonAsync<DeviceRequestResponse>($"/api/v1/auth/device/{typed}", Browser.Json, Token);
        Assert.Equal("Studio PC", shown!.DeviceName);
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = typed })).Status(HttpStatusCode.NoContent);
        // Decided: the code is no longer pending.
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/deny", new { userCode = typed })).Status(HttpStatusCode.NotFound);

        await RewindAsync(code.DeviceCode);
        var response = await PollAsync(desktop, code.DeviceCode);
        response.Status(HttpStatusCode.OK);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        (await SendAsync(desktop, HttpMethod.Get, "/api/v1/auth/me", tokens.AccessToken)).Status(HttpStatusCode.OK);
        var devices = await user.Http.GetFromJsonAsync<List<DeviceResponse>>("/api/v1/auth/devices", Browser.Json, Token);
        Assert.Equal("Studio PC", Assert.Single(devices!).DeviceName);

        // The device code works once.
        await RewindAsync(code.DeviceCode);
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Expired, await PollErrorAsync(desktop, code.DeviceCode));
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(code.DeviceCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_denied_expired_or_forged_code_never_signs_a_device_in()
    {
        var user = new Browser(factory);
        await user.SignUpAndLoginAsync(Browser.Unique("dj"));
        var desktop = factory.CreateClient();

        var denied = await StartAsync(desktop);
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/deny", new { userCode = denied.UserCode })).Status(HttpStatusCode.NoContent);
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Denied, await PollErrorAsync(desktop, denied.DeviceCode));

        var expired = await StartAsync(desktop);
        await RewindAsync(expired.DeviceCode, expire: true);
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = expired.UserCode })).Status(HttpStatusCode.NotFound);
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Expired, await PollErrorAsync(desktop, expired.DeviceCode));

        var real = await StartAsync(desktop);
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = real.UserCode })).Status(HttpStatusCode.NoContent);
        var forged = $"{real.DeviceCode[..real.DeviceCode.IndexOf('.', StringComparison.Ordinal)]}.{Secrets.New()}";
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Expired, await PollErrorAsync(desktop, forged));
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Expired, await PollErrorAsync(desktop, "not-a-device-code"));

        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = "BCDF-GHJ1" })).Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Approving_a_code_needs_a_signed_in_user_and_the_antiforgery_token()
    {
        var desktop = factory.CreateClient();
        var code = await StartAsync(desktop);
        var anonymous = new Browser(factory);
        (await anonymous.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = code.UserCode }))
            .Status(HttpStatusCode.Unauthorized);

        var user = new Browser(factory);
        await user.SignUpAndLoginAsync(Browser.Unique("dj"));
        (await user.SendAsync(HttpMethod.Post, "/api/v1/auth/device/approve", new { userCode = code.UserCode }, antiforgery: false))
            .Status(HttpStatusCode.BadRequest);
        await RewindAsync(code.DeviceCode);
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Pending, await PollErrorAsync(desktop, code.DeviceCode));
    }

    [Fact]
    public async Task A_device_token_cannot_approve_another_device()
    {
        var user = new Browser(factory);
        await user.SignUpAndLoginAsync(Browser.Unique("dj"));
        var desktop = factory.CreateClient();
        var tokens = await SignInDeviceAsync(user, desktop);
        var code = await StartAsync(desktop, "Intruder");

        (await SendAsync(desktop, HttpMethod.Get, $"/api/v1/auth/device/{code.UserCode}", tokens.AccessToken)).Status(HttpStatusCode.Unauthorized);
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/device/approve")
        {
            Content = JsonContent.Create(new { userCode = code.UserCode }),
        })
        {
            request.Headers.Authorization = Browser.Bearer(tokens.AccessToken);
            (await desktop.SendAsync(request, Token)).Status(HttpStatusCode.Unauthorized);
        }
        // A bearer header next to the browser's cookie does not skip the antiforgery check either.
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/device/approve")
        {
            Content = JsonContent.Create(new { userCode = code.UserCode }),
        })
        {
            request.Headers.Authorization = Browser.Bearer(tokens.AccessToken);
            (await user.Http.SendAsync(request, Token)).Status(HttpStatusCode.BadRequest);
        }
        await RewindAsync(code.DeviceCode);
        Assert.Equal(DeviceAuthorizationEndpoints.Errors.Pending, await PollErrorAsync(desktop, code.DeviceCode));
    }

    [Fact]
    public async Task Expired_requests_are_cleaned_up_without_new_sign_ins()
    {
        var desktop = factory.CreateClient();
        var expired = await StartAsync(desktop);
        var live = await StartAsync(desktop);
        await RewindAsync(expired.DeviceCode, expire: true);

        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        Assert.True(await DeviceAuthorizationCleanup.DeleteExpiredAsync(scopes, DateTimeOffset.UtcNow, Token) >= 1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(DeviceAuthorization.TryGetId(expired.DeviceCode, out var expiredId));
        Assert.True(DeviceAuthorization.TryGetId(live.DeviceCode, out var liveId));
        Assert.False(await db.DeviceAuthorizations.AnyAsync(r => r.Id == expiredId, Token));
        Assert.True(await db.DeviceAuthorizations.AnyAsync(r => r.Id == liveId, Token));
    }

    [Fact]
    public async Task Icecast_refuses_a_device_password_once_its_session_is_over()
    {
        var (owner, tenantId, station) = await StationAsync();
        var desktop = factory.CreateClient();
        var tokens = await SignInDeviceAsync(owner, desktop);
        var target = await TargetAsync(desktop, tokens, tenantId, station.Id);
        var mount = $"/stations/{station.PublicId}/live.mp3";
        Assert.True(await SourceAuthAllowsAsync(mount, target.Username, target.Password));

        // The session expires without any sign-out: nothing revokes the credential row, source auth still refuses it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(DeviceSession.TryGetSessionId(tokens.RefreshToken, out var sessionId));
            await db.DeviceSessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Token);
        }
        Assert.False(await SourceAuthAllowsAsync(mount, target.Username, target.Password));
    }

    /// <summary>Asks source auth the way Icecast does: internal port, node credentials, form body.</summary>
    private async Task<bool> SourceAuthAllowsAsync(string mount, string user, string pass)
    {
        var icecast = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
        icecast.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{SourceAuthNode.Password}")));
        var response = await icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "stream_auth", ["mount"] = mount, ["user"] = user, ["pass"] = pass, ["ip"] = "172.18.0.3",
            ["header.content-type"] = "audio/mpeg",
        }), Token);
        response.Status(HttpStatusCode.OK);
        return response.Headers.Contains("icecast-auth-user");
    }

    [Fact]
    public async Task The_device_lists_the_users_stations_in_every_tenant()
    {
        var (owner, tenantA, radio) = await StationAsync("Radio A");
        var userId = Guid.Parse((await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token)).GetProperty("id").GetString()!);
        var (_, tenantB, other) = await StationAsync("Radio B");
        await Browser.AddMemberAsync(factory, tenantB, userId, Domain.Tenants.MembershipRole.Broadcaster);
        var (_, _, stranger) = await StationAsync("Not mine");
        await Browser.SetPlanAsync(factory, tenantA, "growth");
        var gone = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Deleted", slug = "deleted" });
        gone.Status(HttpStatusCode.Created);
        var deleted = (await gone.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{deleted.Id}")).Status(HttpStatusCode.NoContent);

        var desktop = factory.CreateClient();
        var tokens = await SignInDeviceAsync(owner, desktop);
        var response = await SendAsync(desktop, HttpMethod.Get, "/api/v1/me/stations", tokens.AccessToken);
        response.Status(HttpStatusCode.OK);
        var stations = (await response.Content.ReadFromJsonAsync<List<MyStationResponse>>(Browser.Json, Token))!;

        Assert.Equal(2, stations.Count);
        var mine = stations.Single(s => s.StationId == radio.Id);
        Assert.Equal(tenantA, mine.TenantId);
        Assert.Equal(Domain.Tenants.MembershipRole.Owner, mine.Role);
        var invited = stations.Single(s => s.StationId == other.Id);
        Assert.Equal(tenantB, invited.TenantId);
        Assert.Equal(Domain.Tenants.MembershipRole.Broadcaster, invited.Role);
        Assert.Equal(other.PublicId, invited.PublicId);
        Assert.DoesNotContain(stations, s => s.StationId == stranger.Id || s.StationId == deleted.Id);
    }

    [Fact]
    public async Task The_broadcast_target_has_everything_to_go_live_and_no_infrastructure_secret()
    {
        var (owner, tenantId, station) = await StationAsync();
        var desktop = factory.CreateClient();
        var tokens = await SignInDeviceAsync(owner, desktop);

        var response = await SendAsync(desktop, HttpMethod.Post, $"/api/v1/stations/{station.Id}/broadcast-target", tokens.AccessToken, tenantId);
        response.Status(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(SourceAuthNode.Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain(SourceAuthNode.Username, body, StringComparison.Ordinal);
        Assert.DoesNotContain("tc-stream-1", body, StringComparison.Ordinal);
        var target = JsonSerializer.Deserialize<BroadcastTargetResponse>(body, Browser.Json)!;

        Assert.Equal(station.PublicId, target.Username);
        Assert.Equal(64, target.MaxBitrateKbps);
        Assert.Equal([AudioFormat.Mp3, AudioFormat.Opus], target.Outputs.Select(o => o.Format));
        Assert.Equal(new Uri($"https://ingest.tropicastradio.com/stations/{station.PublicId}/live.mp3"), target.Outputs[0].IngestUrl);
        Assert.Equal(new Uri($"https://ingest.tropicastradio.com/stations/{station.PublicId}/live.opus"), target.Outputs[1].IngestUrl);
        Assert.Equal("audio/mpeg", target.Outputs[0].ContentType);
        Assert.Equal("audio/ogg", target.Outputs[1].ContentType);
        Assert.Equal(station.ListenerUrls.Opus, target.Outputs[1].ListenerUrl);

        // Only the hash is stored, bound to the device session.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credential = await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == target.CredentialId, Token);
        Assert.Equal(Secrets.Hash(target.Password), credential.SecretHash);
        Assert.Equal("Studio PC", credential.DeviceLabel);
        Assert.NotNull(credential.DeviceSessionId);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(target.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Asking_again_replaces_the_devices_password_and_signing_out_revokes_it()
    {
        var (owner, tenantId, station) = await StationAsync();
        // An admin-issued credential with the same label is independent of the device's own.
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "Studio PC" }))
            .Status(HttpStatusCode.Created);
        var desktop = factory.CreateClient();
        var studio = await SignInDeviceAsync(owner, desktop);
        var laptop = await SignInDeviceAsync(owner, desktop, "Laptop");

        var first = await TargetAsync(desktop, studio, tenantId, station.Id);
        var second = await TargetAsync(desktop, studio, tenantId, station.Id);
        var other = await TargetAsync(desktop, laptop, tenantId, station.Id);
        Assert.NotEqual(first.Password, second.Password);

        async Task<Dictionary<Guid, CredentialResponse>> CredentialsAsync()
            => (await owner.Http.GetFromJsonAsync<List<CredentialResponse>>($"/api/v1/stations/{station.Id}/credentials", Browser.Json, Token))!
                .ToDictionary(c => c.Id);
        var credentials = await CredentialsAsync();
        Assert.NotNull(credentials[first.CredentialId].RevokedAt);
        Assert.Null(credentials[second.CredentialId].RevokedAt);
        Assert.Null(credentials[other.CredentialId].RevokedAt);

        (await desktop.PostAsJsonAsync("/api/v1/auth/token/revoke", new { refreshToken = studio.RefreshToken }, Token))
            .Status(HttpStatusCode.NoContent);
        credentials = await CredentialsAsync();
        Assert.NotNull(credentials[second.CredentialId].RevokedAt);
        Assert.Null(credentials[other.CredentialId].RevokedAt);
        Assert.Equal(2, credentials.Values.Count(c => c.RevokedAt is null));

        // A signed-out device's access token still validates for a few minutes, but gets no new password.
        (await SendAsync(desktop, HttpMethod.Post, $"/api/v1/stations/{station.Id}/broadcast-target", studio.AccessToken, tenantId))
            .Status(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_a_desktop_device_of_a_member_gets_a_broadcast_target()
    {
        var (owner, tenantId, station) = await StationAsync();
        // The SPA's cookie session is not a device.
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/broadcast-target")).Status(HttpStatusCode.Forbidden);

        var (stranger, strangerTenant, _) = await StationAsync();
        var desktop = factory.CreateClient();
        var tokens = await SignInDeviceAsync(stranger, desktop);
        (await SendAsync(desktop, HttpMethod.Post, $"/api/v1/stations/{station.Id}/broadcast-target", tokens.AccessToken, tenantId))
            .Status(HttpStatusCode.Forbidden);
        (await SendAsync(desktop, HttpMethod.Post, $"/api/v1/stations/{station.Id}/broadcast-target", tokens.AccessToken, strangerTenant))
            .Status(HttpStatusCode.NotFound);
    }
}
