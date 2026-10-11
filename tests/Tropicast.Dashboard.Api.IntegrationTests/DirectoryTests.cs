using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Directories;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>RadioBrowser listing (#13): opt-in, plan-gated, added once when live, never blocking anything.</summary>
public sealed class DirectoryTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Browser Owner, Guid TenantId, StationResponse Station)> StationAsync(string plan = "starter", string? country = "MG",
        bool listed = true)
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        await Browser.SetPlanAsync(factory, tenantId, plan);
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new
        {
            name = "Radio Mada", slug = "radio-mada", genre = "Salegy", country, language = "fr", listInDirectory = listed,
        });
        response.Status(HttpStatusCode.Created);
        return (owner, tenantId, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    private async Task PublishAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryPublisher>().PublishAsync(Token);
    }

    private async Task GoLiveAsync(Guid stationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LiveSessions.Add(LiveSession.Start(stationId, AudioFormat.Mp3, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Token);
    }

    private static async Task<DirectoryStatusResponse> StatusAsync(Browser owner, Guid stationId)
    {
        var response = await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{stationId}/directory");
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DirectoryStatusResponse>(Browser.Json, Token))!;
    }

    private int AddedFor(StationResponse station)
        => factory.Directory.Added.Count(e => e.StreamUrl.AbsolutePath == $"/stations/{station.PublicId}/live.mp3");

    [Fact]
    public async Task An_opted_in_station_is_added_once_when_live_with_only_public_data()
    {
        var (owner, _, station) = await StationAsync();
        await PublishAsync();
        Assert.Equal(0, AddedFor(station));
        Assert.Equal(DirectoryState.WaitingForLive, (await StatusAsync(owner, station.Id)).State);

        await GoLiveAsync(station.Id);
        await PublishAsync();
        await PublishAsync();

        Assert.Equal(1, AddedFor(station));
        var entry = factory.Directory.Added.Single(e => e.StreamUrl.AbsolutePath == $"/stations/{station.PublicId}/live.mp3");
        Assert.Equal(station.ListenerUrls.Mp3, entry.StreamUrl);
        Assert.Equal(("Radio Mada", "salegy", "MG", "fr"), (entry.Name, entry.Genre, entry.CountryCode, entry.LanguageCodes));
        Assert.Equal("https", entry.StreamUrl.Scheme);
        Assert.DoesNotContain("ingest", JsonSerializer.Serialize(entry), StringComparison.OrdinalIgnoreCase);
        var status = await StatusAsync(owner, station.Id);
        Assert.Equal(DirectoryState.Listed, status.State);
        Assert.NotNull(status.RadioBrowserUuid);
        Assert.Equal($"https://www.radio-browser.info/history/{status.RadioBrowserUuid}", status.ListingUrl!.AbsoluteUri);
        Assert.False(status.Submitting); // Off outside production.
    }

    [Fact]
    public async Task Without_a_country_the_owner_is_told_and_nothing_is_sent()
    {
        var (owner, _, station) = await StationAsync(country: null);
        await GoLiveAsync(station.Id);
        await PublishAsync();

        Assert.Equal(0, AddedFor(station));
        var status = await StatusAsync(owner, station.Id);
        Assert.Equal(DirectoryState.NeedsAction, status.State);
        Assert.Contains("country", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_radiobrowser_outage_is_retried_with_backoff_and_blocks_nothing()
    {
        var (owner, _, station) = await StationAsync();
        await GoLiveAsync(station.Id);
        factory.Directory.Fail = true;
        try
        {
            await PublishAsync();
            var status = await StatusAsync(owner, station.Id);
            Assert.Equal(DirectoryState.Retrying, status.State);
            Assert.Contains("unreachable", status.Message, StringComparison.Ordinal);
            Assert.NotNull(status.NextAttemptAt);

            // Editing the station still works during the outage.
            var current = await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}");
            (await owner.SendIfMatchAsync(HttpMethod.Patch, $"/api/v1/stations/{station.Id}", current.Headers.ETag?.ToString(),
                new { genre = "Hira gasy" })).Status(HttpStatusCode.OK);
        }
        finally
        {
            factory.Directory.Fail = false;
        }
        // Not due yet: no new attempt before the backoff.
        await PublishAsync();
        Assert.Equal(0, AddedFor(station));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DirectoryListings.Where(l => l.StationId == station.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Token);
        }
        await PublishAsync();
        Assert.Equal(1, AddedFor(station));
        Assert.Equal(DirectoryState.Listed, (await StatusAsync(owner, station.Id)).State);
    }

    [Fact]
    public async Task The_free_plan_does_not_list_stations()
    {
        var (owner, _, station) = await StationAsync(plan: "free");
        await GoLiveAsync(station.Id);
        await PublishAsync();

        Assert.Equal(0, AddedFor(station));
        Assert.False(station.ListInDirectory);
        Assert.Equal(DirectoryState.NotIncluded, (await StatusAsync(owner, station.Id)).State);
    }

    [Fact]
    public async Task The_node_gets_the_listing_metadata_and_a_delisting_after_opt_out()
    {
        var (owner, _, station) = await StationAsync();
        await GoLiveAsync(station.Id);
        await PublishAsync();

        async Task<JsonElement> DirectoryOnNodeAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Reconciler>().ReconcileAsync(Token);
            using var json = JsonDocument.Parse(factory.Node.Applied[^1]);
            return json.RootElement.GetProperty("stations").GetProperty(station.PublicId).GetProperty("directory").Clone();
        }
        var listed = await DirectoryOnNodeAsync();
        Assert.True(listed.GetProperty("listed").GetBoolean());
        Assert.Equal("Radio Mada", listed.GetProperty("name").GetString());
        Assert.Equal(station.ListenerUrls.Mp3.AbsoluteUri, listed.GetProperty("main_stream_url").GetString());

        var current = await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}");
        (await owner.SendIfMatchAsync(HttpMethod.Patch, $"/api/v1/stations/{station.Id}", current.Headers.ETag?.ToString(),
            new { listInDirectory = false })).Status(HttpStatusCode.OK);

        var delisted = await DirectoryOnNodeAsync();
        Assert.False(delisted.GetProperty("listed").GetBoolean());
        Assert.Equal(DirectoryState.Delisting, (await StatusAsync(owner, station.Id)).State);
        // Opting out never adds again.
        await PublishAsync();
        Assert.Equal(1, AddedFor(station));
    }
}
