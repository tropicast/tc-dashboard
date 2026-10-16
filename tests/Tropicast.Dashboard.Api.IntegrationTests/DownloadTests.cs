using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Downloads;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Outbox;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Desktop app downloads from the mirrored release, and the welcome email that carries them (#127).</summary>
public sealed class DownloadTests : IClassFixture<DashboardFactory>
{
    private readonly DashboardFactory _factory;
    private readonly PostgresContainer _postgres;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public DownloadTests(DashboardFactory factory, PostgresContainer postgres)
    {
        _factory = factory;
        _postgres = postgres;
        PutManifest(factory.Storage);
    }

    /// <summary>What tc-station's mirror-release.sh writes, plus an entry a broken mirror could write.</summary>
    private static void PutManifest(FakeObjectStorage storage)
    {
        static string File(string name, string platform, long size) =>
            $$"""{"name":"{{name}}","platform":"{{platform}}","contentType":"application/octet-stream","size":{{size}},"sha256":"{{new string('a', 64)}}"}""";
        var manifest = $$"""
            {"version":"0.2.1","tag":"v0.2.1","mirroredAt":"2026-10-16T18:27:31+00:00","files":[
              {{File("README.md", "readme", 4414)}},
              {{File("Tropicast-Station-0.2.1-x86_64.AppImage", "linux-x64", 121272824)}},
              {{File("Tropicast-Station-0.2.1-win-x64.msix", "win-x64", 100543689)}},
              {{File("Tropicast-Station-0.2.1-osx-arm64.dmg", "osx-arm64", 76346504)}},
              {{File("Tropicast-Station-0.2.1-osx-x64.dmg", "osx-x64", 78331896)}},
              {{File("../../stations/x/tracks/y", "script", 10)}},
              {{File("evil.exe", "plugin", 10)}}
            ]}
            """;
        storage.Put(StationReleases.ManifestKey, Encoding.UTF8.GetBytes(manifest), "application/json");
    }

    private static HttpClient NoRedirects(DashboardFactory factory)
        => factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task The_list_offers_the_installers_first_with_links_on_this_site()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/downloads/station", Token);

        response.Status(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<StationDownloadsResponse>(Browser.Json, Token))!;
        Assert.Equal("0.2.1", body.Version);
        Assert.Equal(["win-x64", "osx-arm64", "osx-x64", "linux-x64", "readme"], body.Files.Select(f => f.Platform));
        Assert.Equal(new Uri("https://app.test/download/station/win-x64"), body.Files[0].Url);
        Assert.Equal(100543689, body.Files[0].Size);
    }

    [Fact]
    public async Task A_download_redirects_to_a_short_lived_link_that_saves_the_file_under_its_name()
    {
        var response = await NoRedirects(_factory).GetAsync("/download/station/win-x64", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var location = response.Headers.Location!.AbsoluteUri;
        Assert.StartsWith("https://bucket.test/releases/tc-station/latest/Tropicast-Station-0.2.1-win-x64.msix?method=GET&ttl=600", location,
            StringComparison.Ordinal);
        Assert.EndsWith("&download=Tropicast-Station-0.2.1-win-x64.msix", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_platform_is_not_found_and_a_file_the_manifest_does_not_hold_falls_back_to_github()
    {
        var client = NoRedirects(_factory);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/download/station/plugin", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/download/station/manifest.json", Token)).StatusCode);
        // The manifest's "script" entry names a path outside the release: ignored.
        var script = await client.GetAsync("/download/station/script", Token);
        Assert.Equal(HttpStatusCode.Redirect, script.StatusCode);
        Assert.Equal(StationReleases.GitHubReleases, script.Headers.Location);
    }

    [Fact]
    public async Task Without_a_mirrored_release_links_point_to_github()
    {
        await using var empty = new DashboardFactory(_postgres);
        await empty.InitializeAsync();

        var list = (await empty.CreateClient().GetFromJsonAsync<StationDownloadsResponse>("/api/v1/downloads/station", Browser.Json, Token))!;
        var download = await NoRedirects(empty).GetAsync("/download/station/win-x64", Token);

        Assert.Null(list.Version);
        Assert.Empty(list.Files);
        Assert.Equal(StationReleases.GitHubReleases, list.ReleasesPage);
        Assert.Equal(HttpStatusCode.Redirect, download.StatusCode);
        Assert.Equal(StationReleases.GitHubReleases, download.Headers.Location);
    }

    [Fact]
    public async Task Creating_a_workspace_sends_its_creator_one_welcome_email_with_the_download_links()
    {
        var owner = new Browser(_factory);
        var email = Browser.Unique("owner");
        await owner.SignUpAndLoginAsync(email);
        var tenantId = await owner.CreateTenantAsync();
        var member = Browser.Unique("member");
        await Browser.AddMemberAsync(_factory, tenantId, await new Browser(_factory).SignUpAsync(member), MembershipRole.Broadcaster);

        await DispatchAsync();
        await DispatchAsync();

        var welcome = Assert.Single(_factory.Emails.Sent, m => m.To == email && m.Subject.Contains("is ready", StringComparison.Ordinal));
        Assert.Contains("Version 0.2.1", welcome.TextBody, StringComparison.Ordinal);
        Assert.Contains("Windows 10 or 11: https://app.test/download/station/win-x64", welcome.TextBody, StringComparison.Ordinal);
        Assert.Contains("macOS, Apple silicon (M1 or later): https://app.test/download/station/osx-arm64", welcome.TextBody,
            StringComparison.Ordinal);
        Assert.Contains("https://app.test/download/station/readme", welcome.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://app.test/device", welcome.TextBody, StringComparison.Ordinal);
        // Joining a workspace is not creating one.
        Assert.DoesNotContain(_factory.Emails.Sent, m => m.To == member && m.Subject.Contains("is ready", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_mirrored_release_the_email_links_to_github()
    {
        var message = WelcomeEmail.Message("a@example.test", "https://app.test/", "Radio Mada",
            new StationDownloadsResponse(null, StationReleases.GitHubReleases, []));

        Assert.Equal("Your Tropicast workspace Radio Mada is ready", message.Subject);
        Assert.Contains($"Download it for your computer: {StationReleases.GitHubReleases}", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("   https://app.test/stations", message.TextBody, StringComparison.Ordinal);
    }

    private async Task DispatchAsync()
    {
        while (await _factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token) > 0)
        {
        }
    }
}
