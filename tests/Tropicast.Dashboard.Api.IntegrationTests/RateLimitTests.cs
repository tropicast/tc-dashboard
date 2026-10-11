using System.Net;
using System.Net.Http.Json;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class RateLimitTests(PostgresContainer postgres) : IAsyncLifetime
{
    private readonly DashboardFactory _factory = new(postgres)
    {
        AuthPermitLimit = 3,
        Settings = new Dictionary<string, string>
        {
            ["RateLimits:PublicPermitLimit"] = "3", ["RateLimits:DevicePollPermitLimit"] = "3", ["RateLimits:SourceAuthPermitLimit"] = "3",
        },
    };

    public ValueTask InitializeAsync() => _factory.InitializeAsync();
    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Credential_endpoints_are_rate_limited_per_client()
    {
        var client = _factory.CreateBrowser();
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@example.test", password = "nope" },
                TestContext.Current.CancellationToken)).Status(HttpStatusCode.Unauthorized);
        }
        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@example.test", password = "nope" },
            TestContext.Current.CancellationToken);
        limited.Status(HttpStatusCode.TooManyRequests);
        // Health and other endpoints are not limited.
        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).Status(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/v1/public/stations/nobody/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/listen/nobody/nothing", HttpStatusCode.NotFound)]
    [InlineData("POST", "/api/v1/auth/device/token", HttpStatusCode.BadRequest)]
    public async Task Public_and_polling_endpoints_are_rate_limited_per_client(string method, string path, HttpStatusCode normal)
    {
        var client = _factory.CreateBrowser();
        Task<HttpResponseMessage> Send() => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { deviceCode = "nope" }) : null,
        }, TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
        {
            (await Send()).Status(normal);
        }
        (await Send()).Status(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).Status(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Source_auth_is_rate_limited_and_a_limited_call_denies()
    {
        var icecast = _factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
        {
            last = await icecast.PostAsync("/internal/icecast/source-auth",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["mount"] = "/stations/x/live.mp3" }), TestContext.Current.CancellationToken);
        }
        last.Status(HttpStatusCode.TooManyRequests);
        Assert.False(last.Headers.Contains("icecast-auth-user"));
    }
}
