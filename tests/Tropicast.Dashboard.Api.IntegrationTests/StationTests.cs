using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class StationTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<Browser> OwnerAsync(string plan = "growth")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), plan);
        return owner;
    }

    private static async Task<(StationResponse Station, string ETag)> CreateAsync(Browser browser, string slug, object? extra = null)
    {
        var response = await browser.SendAsync(HttpMethod.Post, "/api/v1/stations", extra ?? new { name = "Radio Mada", slug });
        response.Status(HttpStatusCode.Created);
        return ((await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!, response.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task An_admin_creates_a_station_with_a_stream_and_listener_urls()
    {
        var owner = await OwnerAsync();
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations",
            new { name = "Radio Mada", slug = "radio-mada", genre = "Talk", country = "MG", language = "mg" });
        response.Status(HttpStatusCode.Created);
        var station = (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        Assert.Equal($"/api/v1/stations/{station.Id}", response.Headers.Location!.ToString());
        Assert.NotNull(response.Headers.ETag);
        Assert.True(StationPublicId.IsValid(station.PublicId));
        Assert.Equal($"https://listen.tropicastradio.com/stations/{station.PublicId}/live.mp3", station.ListenerUrls.Mp3.ToString());
        Assert.Equal($"https://listen.tropicastradio.com/stations/{station.PublicId}/live.opus", station.ListenerUrls.Opus!.ToString());
        Assert.Equal(("Talk", "MG", "mg"), (station.Genre, station.Country, station.Language));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await db.StreamAssignments.IgnoreQueryFilters().SingleAsync(a => a.StationId == station.Id, Token);
        Assert.Equal(("tc-stream-1", $"/stations/{station.PublicId}"), (assignment.Node, assignment.MountBase));

        var fetched = await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}");
        fetched.Status(HttpStatusCode.OK);
        Assert.Equal(response.Headers.ETag, fetched.Headers.ETag);
    }

    [Fact]
    public async Task A_broadcaster_reads_but_cannot_change_stations_and_anonymous_callers_get_401()
    {
        var owner = await OwnerAsync();
        var (station, etag) = await CreateAsync(owner, "radio");
        var broadcaster = new Browser(factory) { TenantId = owner.TenantId };
        await Browser.AddMemberAsync(factory, owner.TenantId!.Value, await broadcaster.SignUpAndLoginAsync(Browser.Unique("dj")),
            MembershipRole.Broadcaster);

        (await broadcaster.SendAsync(HttpMethod.Get, "/api/v1/stations")).Status(HttpStatusCode.OK);
        (await broadcaster.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.OK);
        (await broadcaster.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "X", slug = "x-station" })).Status(HttpStatusCode.Forbidden);
        (await broadcaster.SendIfMatchAsync(HttpMethod.Patch, $"/api/v1/stations/{station.Id}", etag, new { name = "X" })).Status(HttpStatusCode.Forbidden);
        (await broadcaster.SendIfMatchAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}", etag)).Status(HttpStatusCode.Forbidden);

        var anonymous = factory.CreateBrowser();
        (await anonymous.GetAsync("/api/v1/stations", Token)).Status(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/stations/{station.Id}", Token)).Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_station_endpoint_returns_404_for_a_station_in_another_tenant()
    {
        var other = await OwnerAsync();
        var (foreign, foreignETag) = await CreateAsync(other, "foreign");
        var owner = await OwnerAsync();
        var url = $"/api/v1/stations/{foreign.Id}";

        (await owner.SendAsync(HttpMethod.Get, url)).Status(HttpStatusCode.NotFound);
        (await owner.SendIfMatchAsync(HttpMethod.Patch, url, foreignETag, new { name = "Mine now" })).Status(HttpStatusCode.NotFound);
        (await owner.SendIfMatchAsync(HttpMethod.Delete, url, foreignETag)).Status(HttpStatusCode.NotFound);
        var list = await owner.SendAsync(HttpMethod.Get, "/api/v1/stations");
        Assert.DoesNotContain(foreign.Id.ToString(), await list.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        // Still there for its own tenant.
        (await other.SendAsync(HttpMethod.Get, url)).Status(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_starter_tenant_cannot_create_more_stations_than_its_plan_allows()
    {
        var owner = await OwnerAsync("starter");
        await CreateAsync(owner, "first");
        var refused = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Second", slug = "second" });
        refused.Status(HttpStatusCode.Forbidden);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("stations", problem.GetProperty("limit").GetString());
        Assert.Equal(1, problem.GetProperty("max").GetInt32());
        Assert.Contains("Starter plan allows 1 station", problem.GetProperty("title").GetString(), StringComparison.Ordinal);

        var page = await (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations")).Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Concurrent_creates_cannot_exceed_the_plan()
    {
        var owner = await OwnerAsync("growth");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = $"Radio {i}", slug = $"radio-{i}" })));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Updates_need_the_current_etag()
    {
        var owner = await OwnerAsync();
        var (station, etag) = await CreateAsync(owner, "etag");
        var url = $"/api/v1/stations/{station.Id}";

        (await owner.SendIfMatchAsync(HttpMethod.Patch, url, null, new { name = "New" })).Status(HttpStatusCode.PreconditionRequired);
        var updated = await owner.SendIfMatchAsync(HttpMethod.Patch, url, etag, new { name = "Renamed", genre = "Salegy" });
        updated.Status(HttpStatusCode.OK);
        var body = (await updated.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        Assert.Equal(("Renamed", "Salegy", "etag"), (body.Name, body.Genre, body.Slug));
        Assert.NotEqual(etag, updated.Headers.ETag!.ToString());
        Assert.Equal(station.PublicId, body.PublicId);

        (await owner.SendIfMatchAsync(HttpMethod.Patch, url, etag, new { name = "Lost update" })).Status(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Invalid_fields_and_duplicate_slugs_are_rejected()
    {
        var owner = await OwnerAsync();
        var invalid = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations",
            new { name = "", slug = "Bad Slug", country = "Madagascar", website = "ftp://example.test" });
        invalid.Status(HttpStatusCode.BadRequest);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        foreach (var field in new[] { "name", "slug", "country", "website" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }

        await CreateAsync(owner, "taken");
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Copy", slug = "taken" })).Status(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_hides_the_station_and_frees_its_slug()
    {
        var owner = await OwnerAsync();
        var (station, etag) = await CreateAsync(owner, "gone");
        (await owner.SendIfMatchAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}", etag)).Status(HttpStatusCode.NoContent);
        (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NotFound);
        var (replacement, _) = await CreateAsync(owner, "gone");
        Assert.NotEqual(station.PublicId, replacement.PublicId);
    }

    [Fact]
    public async Task Lists_are_paged()
    {
        var owner = await OwnerAsync("growth");
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(owner, $"paged-{i}");
        }
        var first = await (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations?page=1&pageSize=2")).Content.ReadFromJsonAsync<JsonElement>(Token);
        var second = await (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations?page=2&pageSize=2")).Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((2, 3), (first.GetProperty("items").GetArrayLength(), first.GetProperty("totalCount").GetInt32()));
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations?pageSize=0")).Status(HttpStatusCode.BadRequest);
        (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations?pageSize=101")).Status(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Directory_listing_and_opus_follow_the_plan()
    {
        var owner = await OwnerAsync("free");
        var (station, _) = await CreateAsync(owner, "free-radio", new { name = "Free Radio", slug = "free-radio", listInDirectory = true });
        Assert.False(station.ListInDirectory);
        Assert.NotNull(station.ListenerUrls.Opus);
    }

    [Fact]
    public async Task Station_changes_are_written_to_the_outbox_with_the_change()
    {
        var owner = await OwnerAsync();
        var (station, etag) = await CreateAsync(owner, "events");
        var url = $"/api/v1/stations/{station.Id}";
        var patched = await owner.SendIfMatchAsync(HttpMethod.Patch, url, etag, new { description = "News all day" });
        (await owner.SendIfMatchAsync(HttpMethod.Delete, url, patched.Headers.ETag!.ToString())).Status(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = await db.OutboxMessages
            .Where(m => EF.Functions.JsonContains(m.Payload, $"{{\"stationId\":\"{station.Id}\"}}"))
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(Token);
        Assert.Equal(["StationCreated", "StationChanged", "StationDeleted"], messages.Select(m => m.Type));
        Assert.All(messages, m => Assert.Contains(station.PublicId, m.Payload, StringComparison.Ordinal));
    }
}
