using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>The collector samples the node (a fake exporter here), and the endpoints show what it recorded.</summary>
public sealed class StatsTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Browser Owner, StationResponse Station)> StationAsync()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await owner.CreateTenantAsync();
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        response.Status(HttpStatusCode.Created);
        return (owner, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    /// <summary>A collector of its own, so samples from other tests are not its "previous" one.</summary>
    private StatsCollector Collector() => ActivatorUtilities.CreateInstance<StatsCollector>(factory.Services);

    private async Task SampleAsync(StatsCollector collector, DateTimeOffset at, params MountSample[] mounts)
    {
        factory.Stats.Next = new NodeSample("tc-stream-1", at, true, mounts, 3_200_000_000_000, 22_000_000_000_000);
        await collector.SampleOnceAsync(Token);
    }

    private static async Task<T> GetAsync<T>(Browser browser, string url)
    {
        var response = await browser.SendAsync(HttpMethod.Get, url);
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(Browser.Json, Token))!;
    }

    [Fact]
    public async Task A_station_shows_live_at_the_next_sample_and_its_session_ends_with_the_mount()
    {
        var (owner, station) = await StationAsync();
        var collector = Collector();
        var status = await GetAsync<StationStatusResponse>(owner, $"/api/v1/stations/{station.Id}/status");
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        await SampleAsync(collector, DateTimeOffset.UtcNow,
            new MountSample(station.PublicId, AudioFormat.Mp3, 12, 128, 0, startedAt),
            new MountSample(station.PublicId, AudioFormat.Opus, 3, 0, 0, startedAt));

        status = await GetAsync<StationStatusResponse>(owner, $"/api/v1/stations/{station.Id}/status");
        Assert.Equal((true, 15, false), (status.Live, status.Listeners, status.Stale));
        Assert.Equal([AudioFormat.Mp3, AudioFormat.Opus], status.Outputs.Select(o => o.Format));
        Assert.Equal(128, status.Outputs[0].BitrateKbps);
        Assert.Null(status.Outputs[1].BitrateKbps);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await db.LiveSessions.IgnoreQueryFilters().Where(l => l.StationId == station.Id).ToListAsync(Token);
            Assert.Equal(2, sessions.Count);
            Assert.All(sessions, s => Assert.Null(s.EndedAt));
        }

        await SampleAsync(collector, DateTimeOffset.UtcNow.AddSeconds(1));
        status = await GetAsync<StationStatusResponse>(owner, $"/api/v1/stations/{station.Id}/status");
        Assert.Equal((false, 0), (status.Live, status.Listeners));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.All(await db.LiveSessions.IgnoreQueryFilters().Where(l => l.StationId == station.Id).ToListAsync(Token),
                s => Assert.NotNull(s.EndedAt));
        }
    }

    [Fact]
    public async Task A_session_that_source_auth_just_opened_is_not_ended_by_an_older_view()
    {
        var (_, station) = await StationAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LiveSessions.Add(LiveSession.Start(station.Id, AudioFormat.Mp3, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }

        await SampleAsync(Collector(), DateTimeOffset.UtcNow);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.LiveSessions.IgnoreQueryFilters().SingleAsync(l => l.StationId == station.Id, Token)).EndedAt);
        }
    }

    [Fact]
    public async Task Rollups_add_up_listener_hours_and_egress_per_five_minutes_and_day()
    {
        var (owner, station) = await StationAsync();
        var collector = Collector();
        var start = DateTimeOffset.UtcNow;
        var mount = new MountSample(station.PublicId, AudioFormat.Mp3, 100, 128, 0, start.AddMinutes(-5));
        await SampleAsync(collector, start, mount);
        await SampleAsync(collector, start.AddSeconds(15), mount with { Listeners = 120 });
        await SampleAsync(collector, start.AddSeconds(30), mount with { Listeners = 80 });

        var expectedHours = (120 + 80) * 15 / 3600.0;
        var expectedEgress = (long)Math.Round(128_000 / 8.0 * 120 * 15 * 1.10) + (long)Math.Round(128_000 / 8.0 * 80 * 15 * 1.10);
        var day = await GetAsync<StationStatsResponse>(owner, $"/api/v1/stations/{station.Id}/stats?interval=Day");
        var point = Assert.Single(day.Points);
        Assert.Equal(120, point.PeakListeners);
        Assert.Equal(expectedHours, point.ListenerHours, 6);
        Assert.Equal(expectedEgress, point.EgressBytes);

        var five = await GetAsync<StationStatsResponse>(owner,
            $"/api/v1/stations/{station.Id}/stats?interval=FiveMinutes&from={Uri.EscapeDataString(start.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(start.AddHours(1).ToString("O"))}");
        Assert.Equal(expectedHours, five.ListenerHours, 6);
        Assert.Equal(expectedEgress, five.EgressBytes);
        Assert.Equal(120, five.PeakListeners);
    }

    [Fact]
    public async Task Five_minute_rollups_expire_after_30_days_and_daily_ones_stay()
    {
        var (_, station) = await StationAsync();
        var old = ListenerAccounting.DayPeriod(DateTimeOffset.UtcNow.AddDays(-40));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.FiveMinutes, old, 5, 1, 100));
        db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, old, 5, 1, 100));
        await db.SaveChangesAsync(Token);

        await SampleAsync(Collector(), DateTimeOffset.UtcNow);

        db.ChangeTracker.Clear();
        var left = await db.StationStatsRollups.IgnoreQueryFilters().Where(r => r.StationId == station.Id).ToListAsync(Token);
        Assert.Equal(RollupInterval.Day, Assert.Single(left).Interval);
    }

    [Fact]
    public async Task Stats_ranges_are_bounded_and_other_tenants_stations_are_invisible()
    {
        var (owner, station) = await StationAsync();
        (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/stats?interval=FiveMinutes&from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z"))
            .Status(HttpStatusCode.BadRequest);
        (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/stats?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z"))
            .Status(HttpStatusCode.BadRequest);

        var (stranger, _) = await StationAsync();
        (await stranger.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/status")).Status(HttpStatusCode.NotFound);
        (await stranger.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/stats")).Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_old_sample_makes_the_status_unknown_and_the_operator_sees_node_traffic()
    {
        var (owner, station) = await StationAsync();
        await SampleAsync(Collector(), DateTimeOffset.UtcNow.AddMinutes(-5),
            new MountSample(station.PublicId, AudioFormat.Mp3, 4, 128, 0, null));

        var status = await GetAsync<StationStatusResponse>(owner, $"/api/v1/stations/{station.Id}/status");
        Assert.Equal(((bool?)null, true, 0), (status.Live, status.Stale, status.Outputs.Count));

        (await owner.SendAsync(HttpMethod.Get, "/api/v1/operator/streaming-nodes/tc-stream-1/traffic")).Status(HttpStatusCode.Forbidden);
        var operatorBrowser = new Browser(factory);
        await operatorBrowser.SignUpAndLoginAsync("operator@example.test");
        var traffic = await GetAsync<NodeTrafficResponse>(operatorBrowser, "/api/v1/operator/streaming-nodes/tc-stream-1/traffic");
        Assert.Equal((3_200_000_000_000L, 22_000_000_000_000L, 14.55), (traffic.EgressBytes, traffic.IncludedBytes, traffic.UsedPercent));
        (await operatorBrowser.SendAsync(HttpMethod.Get, "/api/v1/operator/streaming-nodes/tc-stream-9/traffic")).Status(HttpStatusCode.NotFound);
    }
}
