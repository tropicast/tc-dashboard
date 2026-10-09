using System.Net;
using System.Net.Http.Json;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class ApiTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private readonly HttpClient _client = factory.CreateBrowser();

    [Fact]
    public async Task Health_answers_200()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Version_returns_the_build_version()
    {
        var version = await _client.GetFromJsonAsync<VersionInfo>("/api/v1/version", TestContext.Current.CancellationToken);
        Assert.Matches(@"^\d+\.\d+\.\d+", version!.Version);
    }

    [Fact]
    public async Task Unknown_api_path_is_a_404_problem_not_the_spa()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/version", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_body_is_a_400_problem()
    {
        using var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/auth/login", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
