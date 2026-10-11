using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Billing;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Billing;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Billing;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>#14: subscriptions drive the plan; webhooks apply once; downgrades take effect everywhere.</summary>
public sealed class BillingTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static int _events;

    private async Task<(Browser Owner, Guid TenantId)> TenantAsync()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        return (owner, await owner.CreateTenantAsync());
    }

    private static BillingEvent Subscription(Guid tenantId, string plan, SubscriptionStatus status, DateTimeOffset createdAt, string? id = null)
        => new(id ?? $"evt_{Interlocked.Increment(ref _events)}", createdAt, "customer.subscription.updated", BillingEventKind.SubscriptionChanged,
            tenantId, $"cus_{tenantId:N}", $"sub_{tenantId:N}",
            new SubscriptionState(plan, status, createdAt.AddMonths(1), false, null, $"cus_{tenantId:N}", $"sub_{tenantId:N}"));

    private Task<HttpResponseMessage> WebhookAsync(BillingEvent billingEvent, string signature = "valid")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhook")
        {
            Content = new StringContent(JsonSerializer.Serialize(billingEvent, FakePaymentProvider.Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", signature);
        return factory.CreateClient().SendAsync(request, Token);
    }

    private async Task<string> PlanOfAsync(Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.PlanId).SingleAsync(Token);
    }

    /// <summary>Acceptance: Free → Starter in test mode raises the station's listener cap on the node.</summary>
    [Fact]
    public async Task Upgrading_free_to_starter_raises_the_listener_cap_on_the_node()
    {
        var (owner, tenantId) = await TenantAsync();
        var station = (await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" }))
            .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;

        var checkout = await owner.SendAsync(HttpMethod.Post, "/api/v1/billing/checkout", new { planId = "starter" });
        checkout.Status(HttpStatusCode.OK);
        Assert.Equal($"https://checkout.stripe.test/c/{tenantId}", (await checkout.Content.ReadFromJsonAsync<RedirectResponse>(Browser.Json, Token))!.Url.AbsoluteUri);
        var request = factory.Payments.Checkouts.Single(c => c.TenantId == tenantId);
        Assert.Equal(("starter", 14), (request.PlanId, request.TrialDays));

        (await WebhookAsync(Subscription(tenantId, "starter", SubscriptionStatus.Trialing, DateTimeOffset.UtcNow))).Status(HttpStatusCode.OK);

        Assert.Equal("starter", await PlanOfAsync(tenantId));
        await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Reconciler>().ReconcileAsync(Token);
        }
        using var stations = JsonDocument.Parse(factory.Node.Applied[^1]);
        Assert.Equal(Plan.Starter.MaxListeners, stations.RootElement.GetProperty("stations").GetProperty(station.PublicId).GetProperty("max_listeners").GetInt32());

        var billing = await owner.Http.GetFromJsonAsync<BillingResponse>("/api/v1/billing", Browser.Json, Token);
        Assert.Equal(("starter", SubscriptionStatus.Trialing, false, true), (billing!.PlanId, billing.Status, billing.TrialAvailable, billing.CanManage));
        Assert.Equal("€9 / month", billing.Plans.Single(p => p.Id == "starter").Price);
        // A subscribed tenant changes plan in the portal, not with a second checkout.
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/billing/checkout", new { planId = "growth" })).Status(HttpStatusCode.Conflict);
        var portal = await owner.SendAsync(HttpMethod.Post, "/api/v1/billing/portal");
        Assert.Equal($"https://billing.stripe.test/p/cus_{tenantId:N}", (await portal.Content.ReadFromJsonAsync<RedirectResponse>(Browser.Json, Token))!.Url.AbsoluteUri);
    }

    /// <summary>Acceptance: replayed webhooks do not change state twice.</summary>
    [Fact]
    public async Task Replayed_and_late_webhooks_change_nothing()
    {
        var (_, tenantId) = await TenantAsync();
        var start = DateTimeOffset.UtcNow;
        var active = Subscription(tenantId, "growth", SubscriptionStatus.Active, start);
        (await WebhookAsync(active)).Status(HttpStatusCode.OK);
        var canceled = Subscription(tenantId, "growth", SubscriptionStatus.Canceled, start.AddMinutes(5));
        (await WebhookAsync(canceled)).Status(HttpStatusCode.OK);
        Assert.Equal(Plan.Free.Id, await PlanOfAsync(tenantId));

        // The same "active" event delivered again, and an older "active" event arriving late.
        (await WebhookAsync(active)).Status(HttpStatusCode.OK);
        (await WebhookAsync(Subscription(tenantId, "growth", SubscriptionStatus.Active, start.AddMinutes(1)))).Status(HttpStatusCode.OK);

        Assert.Equal(Plan.Free.Id, await PlanOfAsync(tenantId));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(SubscriptionStatus.Canceled, (await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId, Token)).Status);
        Assert.Equal(1, await db.ProcessedWebhookEvents.CountAsync(e => e.Id == active.Id, Token));
        // One plan change up and one down: the replay raised no event.
        var planChanges = await db.OutboxMessages.Where(m => m.Type == "TenantPlanChanged").Select(m => m.Payload).ToListAsync(Token);
        Assert.Equal(2, planChanges.Count(p => p.Contains(tenantId.ToString(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_bad_signature_is_refused()
        => (await WebhookAsync(Subscription(Guid.NewGuid(), "starter", SubscriptionStatus.Active, DateTimeOffset.UtcNow), "forged"))
            .Status(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_failed_payment_keeps_the_plan_for_the_grace_period_then_free()
    {
        var (_, tenantId) = await TenantAsync();
        var start = DateTimeOffset.UtcNow;
        (await WebhookAsync(Subscription(tenantId, "starter", SubscriptionStatus.Active, start))).Status(HttpStatusCode.OK);
        (await WebhookAsync(Subscription(tenantId, "starter", SubscriptionStatus.PastDue, start.AddSeconds(1)))).Status(HttpStatusCode.OK);
        Assert.Equal("starter", await PlanOfAsync(tenantId));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.PastDueSince, DateTimeOffset.UtcNow.AddDays(-8)), Token);
            await scope.ServiceProvider.GetRequiredService<BillingService>().SyncPlansAsync(Token);
        }
        Assert.Equal(Plan.Free.Id, await PlanOfAsync(tenantId));
    }

    [Fact]
    public async Task After_a_downgrade_stations_beyond_the_plan_cannot_broadcast()
    {
        var (owner, tenantId) = await TenantAsync();
        await Browser.SetPlanAsync(factory, tenantId, "growth");
        var stations = new List<StationResponse>();
        foreach (var slug in new[] { "first", "second" })
        {
            stations.Add((await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = slug, slug }))
                .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
        }
        (await WebhookAsync(Subscription(tenantId, "growth", SubscriptionStatus.Canceled, DateTimeOffset.UtcNow))).Status(HttpStatusCode.OK);
        Assert.Equal(Plan.Free.Id, await PlanOfAsync(tenantId));

        async Task<string?> DenialAsync(StationResponse station)
        {
            var issued = await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "PC" }))
                .Content.ReadFromJsonAsync<IssuedCredentialResponse>(Browser.Json, Token);
            var icecast = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
            icecast.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{SourceAuthNode.Password}")));
            var response = await icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "stream_auth", ["mount"] = $"/stations/{station.PublicId}/live.mp3", ["user"] = station.PublicId,
                ["pass"] = issued!.Secret, ["header.ice-bitrate"] = "64",
            }), Token);
            return response.Headers.TryGetValues("icecast-auth-message", out var reason) ? reason.Single() : null;
        }
        Assert.Null(await DenialAsync(stations[0]));
        Assert.Contains("plan allows 1 station", await DenialAsync(stations[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_owners_start_a_checkout_for_a_plan_on_sale()
    {
        var (owner, tenantId) = await TenantAsync();
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/billing/checkout", new { planId = "pro" })).Status(HttpStatusCode.BadRequest);
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/billing/portal")).Status(HttpStatusCode.NotFound);

        var admin = new Browser(factory);
        var adminId = await admin.SignUpAndLoginAsync(Browser.Unique("admin"));
        await Browser.AddMemberAsync(factory, tenantId, adminId, MembershipRole.Admin);
        admin.TenantId = tenantId;
        (await admin.SendAsync(HttpMethod.Post, "/api/v1/billing/checkout", new { planId = "starter" })).Status(HttpStatusCode.Forbidden);
        (await admin.SendAsync(HttpMethod.Get, "/api/v1/billing")).Status(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_usage_report_sums_the_month_per_station()
    {
        var (owner, tenantId) = await TenantAsync();
        var station = (await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" }))
            .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero), 40, 100.5, 2_000));
            db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), 70, 20, 500));
            db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), 99, 1, 1));
            await db.SaveChangesAsync(Token);
        }

        var usage = await owner.Http.GetFromJsonAsync<UsageResponse>("/api/v1/billing/usage?month=2026-09", Browser.Json, Token);

        Assert.Equal(("2026-09", 70, 120.5, 2_500L), (usage!.Month, usage.PeakListeners, usage.ListenerHours, usage.EgressBytes));
        Assert.Equal(station.Id, Assert.Single(usage.Stations).StationId);
        (await owner.SendAsync(HttpMethod.Get, "/api/v1/billing/usage?month=September")).Status(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_oversized_webhook_is_refused_even_without_a_content_length()
    {
        var huge = new MemoryStream(new byte[600 * 1024]);
        // A non-seekable stream: sent chunked, with no Content-Length to check.
        var content = new StreamContent(new NonSeekable(huge));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhook") { Content = content };
        request.Headers.Add("Stripe-Signature", "valid");
        (await factory.CreateClient().SendAsync(request, Token)).Status(HttpStatusCode.RequestEntityTooLarge);
    }

    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task The_operator_report_lists_idle_tenants_with_zero_usage()
    {
        var (_, tenantId) = await TenantAsync();
        var operatorBrowser = new Browser(factory);
        await operatorBrowser.SignUpAndLoginAsync("operator@example.test");
        var report = await operatorBrowser.Http.GetFromJsonAsync<List<TenantUsageResponse>>("/api/v1/operator/usage?month=2026-01", Browser.Json, Token);
        var idle = Assert.Single(report!, r => r.TenantId == tenantId);
        Assert.Equal((0, 0.0, 0L), (idle.PeakListeners, idle.ListenerHours, idle.EgressBytes));
    }

    [Fact]
    public async Task Stations_created_at_the_same_instant_still_respect_the_station_count()
    {
        var (owner, tenantId) = await TenantAsync();
        await Browser.SetPlanAsync(factory, tenantId, "growth");
        var ids = new List<Guid>();
        foreach (var slug in new[] { "twin-a", "twin-b" })
        {
            ids.Add((await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = slug, slug }))
                .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!.Id);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instant = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.Stations.IgnoreQueryFilters().Where(s => ids.Contains(s.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CreatedAt, instant), Token);
        }
        await Browser.SetPlanAsync(factory, tenantId, "free");

        var allowed = 0;
        foreach (var id in ids)
        {
            var station = (await (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{id}")).Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
            var issued = await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{id}/credentials", new { deviceLabel = "PC" }))
                .Content.ReadFromJsonAsync<IssuedCredentialResponse>(Browser.Json, Token);
            var icecast = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost:8081") });
            icecast.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SourceAuthNode.Username}:{SourceAuthNode.Password}")));
            var response = await icecast.PostAsync("/internal/icecast/source-auth", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "stream_auth", ["mount"] = $"/stations/{station.PublicId}/live.mp3", ["user"] = station.PublicId,
                ["pass"] = issued!.Secret, ["header.ice-bitrate"] = "64",
            }), Token);
            allowed += response.Headers.Contains("icecast-auth-user") ? 1 : 0;
        }
        Assert.Equal(1, allowed);
    }
}
