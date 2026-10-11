using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Logging;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Telemetry;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>#16: health, redacted logs, account security events, audit log, metrics.</summary>
public sealed class HardeningTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_needs_nothing_and_ready_checks_the_database()
    {
        var client = factory.CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live", Token));
        Assert.Equal("Healthy", await client.GetStringAsync("/health/ready", Token));
        Assert.Equal("Healthy", await client.GetStringAsync("/health", Token));
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature", "eyJhbGci")]
    [InlineData("Host=db;Password=s3cret-pass;Database=x", "s3cret-pass")]
    [InlineData("Host=db;Password=\"known;secret=x\";Database=x", "known;secret")]
    [InlineData("Host=db;Password='known secret';Database=x", "known secret")]
    [InlineData("Host=db;Password=\"known\"\"secret\";Database=x", "secret")]
    [InlineData("""{"password":"known\"secret","name":"x"}""", "secret")]
    [InlineData("GET /confirm-email?userId=1&code=Q2ZESjhBTmNsZGtz", "Q2ZESjhBTmNsZGtz")]
    [InlineData("""{"refreshToken":"abc.def","name":"x"}""", "abc.def")]
    [InlineData("postgresql://owner:pa55word@ep-x.neon.tech/db", "pa55word")]
    [InlineData("token 0123456789abcdef0123456789abcdef.AbCdEfGhIjKlMnOpQrStUvWxYz012345678", "AbCdEfGhIjKlMnOp")]
    [InlineData("issued kQ3m9x2p7qaZ_-abcdefghijklmnopqrstuvwxy0123 for Studio PC", "kQ3m9x2p7qa")]
    public void Credentials_are_masked_in_log_text(string text, string secret)
    {
        var masked = Redaction.Apply(text);
        Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);
        Assert.Contains("[redacted]", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
        => Assert.Equal("Applied stations 3f2a to tc-stream-1 (4 stations)", Redaction.Apply("Applied stations 3f2a to tc-stream-1 (4 stations)"));

    /// <summary>Acceptance: a search of the shipped logs for a known test credential finds nothing.</summary>
    [Fact]
    public void Production_log_lines_are_json_without_the_credential()
    {
        const string credential = "known-test-credential-PW-1234";
        var formatter = new RedactingJsonFormatter();
        var output = new StringWriter();
        var exception = new InvalidOperationException($"Connection failed: Host=db;Password={credential}");
        var state = new[] { new KeyValuePair<string, object?>("Url", $"https://admin:{credential}@example.test/") };
        formatter.Write(new LogEntry<KeyValuePair<string, object?>[]>(LogLevel.Error, "Test", new EventId(7), state, exception,
            (_, _) => $"Calling https://admin:{credential}@example.test/ failed"), null, output);

        var line = output.ToString();
        Assert.DoesNotContain(credential, line, StringComparison.Ordinal);
        using var json = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal("Error", json.RootElement.GetProperty("level").GetString());
        Assert.Equal("System.InvalidOperationException", json.RootElement.GetProperty("exception").GetString());
    }

    [Fact]
    public async Task Sign_ins_failures_and_device_events_are_in_the_account_activity()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("activity");
        await browser.SignUpAsync(email);
        (await browser.LoginAsync(email, "wrong passphrase for activity")).Status(HttpStatusCode.Unauthorized);
        (await browser.LoginAsync(email)).Status(HttpStatusCode.NoContent);
        var desktop = factory.CreateClient();
        var tokens = await (await desktop.PostAsJsonAsync("/api/v1/auth/token", new { email, password = Browser.Password, deviceName = "Studio PC" }, Token))
            .Content.ReadFromJsonAsync<TokenResponse>(Token);
        var rotated = await (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = tokens!.RefreshToken }, Token))
            .Content.ReadFromJsonAsync<TokenResponse>(Token);
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = tokens.RefreshToken }, Token)).Status(HttpStatusCode.Unauthorized);

        var activity = await browser.Http.GetFromJsonAsync<List<SecurityEventResponse>>("/api/v1/auth/activity", Browser.Json, Token);
        var actions = activity!.Select(a => a.Action).ToList();
        Assert.Equal(["device.token_reuse", "device.signed_in", "signin.succeeded", "signin.failed"], actions);
        Assert.Equal("Studio PC", activity![1].Detail);
        Assert.NotNull(rotated);
    }

    [Fact]
    public async Task The_audit_log_records_stations_members_and_plan_changes()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        var created = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        var station = (await created.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        (await owner.SendIfMatchAsync(HttpMethod.Patch, $"/api/v1/stations/{station.Id}", created.Headers.ETag?.ToString(),
            new { genre = "Talk", description = "x" })).Status(HttpStatusCode.OK);
        var member = new Browser(factory);
        var memberId = await member.SignUpAndLoginAsync(Browser.Unique("member"));
        await Browser.AddMemberAsync(factory, tenantId, memberId, Domain.Tenants.MembershipRole.Broadcaster);
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{memberId}")).Status(HttpStatusCode.NoContent);
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NoContent);
        await Browser.SetPlanAsync(factory, tenantId, "starter");
        await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token);
        await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token);

        var log = (await owner.Http.GetFromJsonAsync<PagedList<AuditEntryResponse>>("/api/v1/tenants/current/audit?pageSize=50", Browser.Json, Token))!;
        var actions = log.Items.Select(e => e.Action).ToList();
        Assert.Equal(["plan.changed", "station.deleted", "member.removed", "station.updated", "station.created"], actions);
        Assert.Equal("description, genre", log.Items.Single(e => e.Action == "station.updated").Summary);
        Assert.Null(log.Items.Single(e => e.Action == "plan.changed").ActorEmail);
        Assert.Equal("starter", log.Items.Single(e => e.Action == "plan.changed").Summary);
        Assert.NotNull(log.Items.Single(e => e.Action == "station.created").ActorEmail);
        // No station values (name, description…) in the log: only IDs and names of changed settings.
        Assert.DoesNotContain(log.Items, e => e.Summary?.Contains("Radio", StringComparison.Ordinal) == true);

        // Broadcasters do not read it.
        var broadcaster = new Browser(factory);
        var broadcasterId = await broadcaster.SignUpAndLoginAsync(Browser.Unique("broadcaster"));
        await Browser.AddMemberAsync(factory, tenantId, broadcasterId, Domain.Tenants.MembershipRole.Broadcaster);
        broadcaster.TenantId = tenantId;
        (await broadcaster.SendAsync(HttpMethod.Get, "/api/v1/tenants/current/audit")).Status(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Source_auth_decisions_are_counted_for_alerting()
    {
        var results = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DashboardMetrics.MeterName && instrument.Name == "tropicast.source_auth.decisions")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "result")
                {
                    lock (results)
                    {
                        results.Add((string)tag.Value!);
                    }
                }
            }
        });
        listener.Start();

        var icecast = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
        icecast.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{SourceAuthNode.Password}")));
        (await icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "stream_auth", ["mount"] = "/stations/nobody/live.mp3", ["user"] = "nobody", ["pass"] = "x",
        }), Token)).Status(HttpStatusCode.OK);

        lock (results)
        {
            Assert.Contains("deny", results);
        }
    }
}
