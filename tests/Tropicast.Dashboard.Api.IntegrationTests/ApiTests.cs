using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

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
}
