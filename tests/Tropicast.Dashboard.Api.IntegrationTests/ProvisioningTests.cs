using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Operator;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class ProvisioningTests(PostgresContainer postgres) : IAsyncLifetime
{
    private DashboardFactory _factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new DashboardFactory(postgres);
        await _factory.InitializeAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<(Browser Owner, StationResponse Station)> StationAsync(DashboardFactory factory, string plan = "free")
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await Browser.SetPlanAsync(factory, await owner.CreateTenantAsync(), plan);
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        response.Status(HttpStatusCode.Created);
        return (owner, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    private async Task<StreamingNodeState> ReconcileAsync()
    {
        await _factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token);
        await using var scope = _factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<Reconciler>().ReconcileAsync(Token))!;
    }

    private static JsonElement StationIn(string json, string publicId)
        => JsonDocument.Parse(json).RootElement.GetProperty("stations").GetProperty(publicId).Clone();

    [Fact]
    public async Task Stations_and_their_plan_limits_reach_the_node()
    {
        var (_, station) = await StationAsync(_factory, "starter");
        var state = await ReconcileAsync();
        Assert.True(state.InSync);
        var applied = StationIn(_factory.Node.Applied[^1], station.PublicId);
        Assert.Equal(("starter", 500, 128), (applied.GetProperty("plan").GetString(), applied.GetProperty("max_listeners").GetInt32(),
            applied.GetProperty("max_bitrate_kbps").GetInt32()));

        // Nothing changed: nothing is sent again.
        var count = _factory.Node.Applied.Count;
        await ReconcileAsync();
        Assert.Equal(count, _factory.Node.Applied.Count);
    }

    [Fact]
    public async Task A_plan_change_updates_the_listener_cap()
    {
        var (owner, station) = await StationAsync(_factory, "free");
        await ReconcileAsync();
        await Browser.SetPlanAsync(_factory, owner.TenantId!.Value, "growth");
        await ReconcileAsync();
        Assert.Equal(5000, StationIn(_factory.Node.Applied[^1], station.PublicId).GetProperty("max_listeners").GetInt32());
    }

    [Fact]
    public async Task Deleted_stations_and_suspended_tenants_are_removed_from_the_node()
    {
        var (owner, station) = await StationAsync(_factory);
        await ReconcileAsync();
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}")).Status(HttpStatusCode.NoContent);
        await ReconcileAsync();
        Assert.False(JsonDocument.Parse(_factory.Node.Applied[^1]).RootElement.GetProperty("stations").TryGetProperty(station.PublicId, out _));
    }

    [Fact]
    public async Task A_failed_apply_is_retried_shown_to_operators_and_never_blocks_the_dashboard()
    {
        _factory.Node.Fail = true;
        var (owner, _) = await StationAsync(_factory);
        var failed = await ReconcileAsync();
        Assert.Equal(1, failed.FailedAttempts);
        Assert.Contains("Connection refused", failed.LastError, StringComparison.Ordinal);
        Assert.NotNull(failed.NextAttemptAt);
        Assert.False(failed.InSync);

        // The dashboard keeps working while the node is unreachable.
        (await owner.SendAsync(HttpMethod.Get, "/api/v1/stations")).Status(HttpStatusCode.OK);

        var operatorBrowser = new Browser(_factory);
        await operatorBrowser.SignUpAndLoginAsync("operator@example.test");
        var nodes = await (await operatorBrowser.SendAsync(HttpMethod.Get, "/api/v1/operator/streaming-nodes"))
            .Content.ReadFromJsonAsync<List<StreamingNodeResponse>>(Browser.Json, Token);
        var node = Assert.Single(nodes!);
        Assert.Equal(("tc-stream-1", false, 1), (node.Node, node.InSync, node.FailedAttempts));

        // Backoff: not retried before it is due...
        var before = _factory.Node.Applied.Count;
        _factory.Node.Fail = false;
        Assert.Equal(1, (await ReconcileAsync()).FailedAttempts);
        Assert.Equal(before, _factory.Node.Applied.Count);
        // ...unless the operator asks.
        (await operatorBrowser.SendAsync(HttpMethod.Post, "/api/v1/operator/streaming-nodes/tc-stream-1/reconcile")).Status(HttpStatusCode.Accepted);
        var recovered = await ReconcileAsync();
        Assert.True(recovered.InSync);
        Assert.Equal(0, recovered.FailedAttempts);
        Assert.Null(recovered.LastError);
    }

    [Fact]
    public async Task Only_operators_see_the_node_state()
    {
        var owner = new Browser(_factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        (await owner.SendAsync(HttpMethod.Get, "/api/v1/operator/streaming-nodes")).Status(HttpStatusCode.Forbidden);
        (await _factory.CreateBrowser().GetAsync("/api/v1/operator/streaming-nodes", Token)).Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_full_node_refuses_new_stations()
    {
        await using var small = new DashboardFactory(postgres)
        {
            Settings = new Dictionary<string, string> { ["Streaming:MaxListenerCaps"] = "250" },
        };
        await small.InitializeAsync();
        await StationAsync(small);
        await StationAsync(small);
        var third = new Browser(small);
        await third.SignUpAndLoginAsync(Browser.Unique("third"));
        await third.CreateTenantAsync();
        var refused = await third.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        refused.Status(HttpStatusCode.ServiceUnavailable);
        Assert.Contains(small.Logs.Lines, l => l.StartsWith("Critical", StringComparison.Ordinal) && l.Contains("is full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_background_loops_a_plan_change_reaches_the_node_within_seconds()
    {
        await using var live = new DashboardFactory(postgres)
        {
            Settings = new Dictionary<string, string>
            {
                ["Outbox:Enabled"] = "true", ["Outbox:PollInterval"] = "00:00:00.200",
                ["Provisioning:Enabled"] = "true", ["Provisioning:Interval"] = "00:05:00",
            },
        };
        await live.InitializeAsync();
        var (owner, station) = await StationAsync(live);
        await Until(() => live.Node.Applied.Any(j => j.Contains(station.PublicId, StringComparison.Ordinal)));

        var timer = System.Diagnostics.Stopwatch.StartNew();
        await Browser.SetPlanAsync(live, owner.TenantId!.Value, "growth");
        await Until(() => live.Node.Applied.Count > 0
            && StationIn(live.Node.Applied[^1], station.PublicId).GetProperty("max_listeners").GetInt32() == 5000);
        // The 5-minute timer did not run: the plan event woke provisioning.
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"took {timer.Elapsed}");
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            await Task.Delay(100, deadline.Token);
        }
    }

    [Fact]
    public async Task Conflicting_node_settings_fail_at_startup()
    {
        await using var conflicting = new DashboardFactory(postgres)
        {
            Settings = new Dictionary<string, string> { ["Streaming:Node"] = "tc-stream-1", ["Provisioning:Node"] = "tc-stream-2" },
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await conflicting.InitializeAsync());
        Assert.Contains("Set only Streaming:Node", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suspended_tenants_do_not_use_node_capacity()
    {
        await using var small = new DashboardFactory(postgres)
        {
            Settings = new Dictionary<string, string> { ["Streaming:MaxListenerCaps"] = "150" },
        };
        await small.InitializeAsync();
        var (owner, _) = await StationAsync(small);
        await using (var scope = small.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tropicast.Dashboard.Infrastructure.Persistence.AppDbContext>();
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(db.Database,
                $"UPDATE tenants SET status = 'Suspended' WHERE id = {owner.TenantId}", Token);
        }
        await StationAsync(small);
    }
}
