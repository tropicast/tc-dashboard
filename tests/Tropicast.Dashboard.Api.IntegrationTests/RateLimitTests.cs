using System.Net;
using System.Net.Http.Json;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class RateLimitTests(PostgresContainer postgres) : IAsyncLifetime
{
    private readonly DashboardFactory _factory = new(postgres) { AuthPermitLimit = 3 };

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
}
