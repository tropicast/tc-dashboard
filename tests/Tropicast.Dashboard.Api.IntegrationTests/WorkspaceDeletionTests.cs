using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>An Owner deletes the whole workspace from the dashboard (#125), as <c>ops/delete-workspace.sh</c> does.</summary>
public sealed class WorkspaceDeletionTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Workspace(Browser Owner, Guid TenantId, string Slug, StationResponse Station, Guid TrackId, string ObjectKey);

    /// <summary>A Starter workspace with a station, a track and its file, a playlist, a credential, a session and stats.</summary>
    private async Task<Workspace> WorkspaceAsync()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        await Browser.SetPlanAsync(factory, tenantId, Plan.Starter.Id);
        var created = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        created.Status(HttpStatusCode.Created);
        var station = (await created.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "BUTT" }))
            .Status(HttpStatusCode.Created);
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/playlists", new { name = "Morning" }))
            .Status(HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var track = Track.StartUpload(station.Id, "song.mp3", 1000, now, TimeSpan.FromMinutes(5));
        track.Confirm(TrackFormat.Mp3, TimeSpan.FromSeconds(180), 1000, "Song", "Artist");
        db.Tracks.Add(track);
        var session = LiveSession.Start(station.Id, AudioFormat.Mp3, now.AddHours(-2));
        session.End(now.AddHours(-1));
        db.LiveSessions.Add(session);
        db.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero), 3, 1.5, 1000));
        await db.SaveChangesAsync(Token);
        factory.Storage.Put(track.ObjectKey, [1, 2, 3]);
        var slug = (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId, Token)).Slug;
        return new Workspace(owner, tenantId, slug, station, track.Id, track.ObjectKey);
    }

    private static async Task<WorkspaceDeletionResponse> ReportAsync(Browser browser)
    {
        var response = await browser.SendAsync(HttpMethod.Get, "/api/v1/tenants/current/deletion");
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WorkspaceDeletionResponse>(Browser.Json, Token))!;
    }

    private static Task<HttpResponseMessage> DeleteAsync(Browser browser, string slug)
        => browser.SendAsync(HttpMethod.Delete, "/api/v1/tenants/current", new { confirmSlug = slug });

    private async Task<bool> TenantExistsAsync(Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Id == tenantId, Token);
    }

    [Fact]
    public async Task The_report_lists_what_goes_and_changes_nothing()
    {
        var workspace = await WorkspaceAsync();
        var dj = new Browser(factory) { TenantId = workspace.TenantId };
        var djEmail = Browser.Unique("dj");
        await Browser.AddMemberAsync(factory, workspace.TenantId, await dj.SignUpAndLoginAsync(djEmail), MembershipRole.Broadcaster);

        var report = await ReportAsync(workspace.Owner);

        Assert.Equal((workspace.TenantId, workspace.Slug, Plan.Starter.Id), (report.Id, report.Slug, report.PlanId));
        Assert.Empty(report.Blockers);
        var station = Assert.Single(report.Stations);
        Assert.Equal((workspace.Station.PublicId, false, false, false),
            (station.PublicId, station.Deleted, station.Live, station.ListedOnRadioBrowser));
        Assert.Equal(2, report.Members.Count);
        Assert.Contains(report.Members, m => m.Email == djEmail && m.Role == MembershipRole.Broadcaster && m.LeftWithoutWorkspace);
        Assert.Equal(new DeletionCountsResponse(Stations: 1, Credentials: 1, LiveSessions: 1, StatsRollups: 1, Tracks: 1, Playlists: 1,
            Members: 2, Invitations: 0, AuditEntries: report.Counts.AuditEntries), report.Counts);
        Assert.True(await TenantExistsAsync(workspace.TenantId));
    }

    [Fact]
    public async Task Only_an_owner_sees_the_report_and_deletes()
    {
        var workspace = await WorkspaceAsync();
        foreach (var role in new[] { MembershipRole.Admin, MembershipRole.Broadcaster })
        {
            var member = new Browser(factory) { TenantId = workspace.TenantId };
            await Browser.AddMemberAsync(factory, workspace.TenantId, await member.SignUpAndLoginAsync(Browser.Unique("member")), role);
            (await member.SendAsync(HttpMethod.Get, "/api/v1/tenants/current/deletion")).Status(HttpStatusCode.Forbidden);
            (await DeleteAsync(member, workspace.Slug)).Status(HttpStatusCode.Forbidden);
        }
        Assert.True(await TenantExistsAsync(workspace.TenantId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("t-")]
    [InlineData("WRONG")]
    public async Task A_wrong_slug_deletes_nothing(string typed)
    {
        var workspace = await WorkspaceAsync();
        var response = await DeleteAsync(workspace.Owner, typed);
        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, response.StatusCode.ToString());
        (await DeleteAsync(workspace.Owner, workspace.Slug.ToUpperInvariant())).Status(HttpStatusCode.UnprocessableEntity);
        Assert.True(await TenantExistsAsync(workspace.TenantId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Incomplete)]
    public async Task A_subscription_stripe_may_charge_for_blocks_the_deletion(SubscriptionStatus status)
    {
        var workspace = await WorkspaceAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Subscriptions.Add(Subscription.Create(workspace.TenantId, Plan.Starter.Id, status, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }

        Assert.Equal([DeletionBlocker.Subscription], (await ReportAsync(workspace.Owner)).Blockers);
        (await DeleteAsync(workspace.Owner, workspace.Slug)).Status(HttpStatusCode.Conflict);
        Assert.True(await TenantExistsAsync(workspace.TenantId));
    }

    [Fact]
    public async Task A_live_station_blocks_the_deletion_even_when_it_went_live_after_the_report()
    {
        var workspace = await WorkspaceAsync();
        Assert.Empty((await ReportAsync(workspace.Owner)).Blockers);
        // The DJ goes live while the dialog is open.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LiveSessions.Add(LiveSession.Start(workspace.Station.Id, AudioFormat.Mp3, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }

        (await DeleteAsync(workspace.Owner, workspace.Slug)).Status(HttpStatusCode.Conflict);
        Assert.True(await TenantExistsAsync(workspace.TenantId));
        Assert.Equal([DeletionBlocker.Live], (await ReportAsync(workspace.Owner)).Blockers);
    }

    [Fact]
    public async Task Deleting_removes_every_row_and_file_and_keeps_the_member_accounts()
    {
        var workspace = await WorkspaceAsync();
        var dj = new Browser(factory) { TenantId = workspace.TenantId };
        var djId = await dj.SignUpAndLoginAsync(Browser.Unique("dj"));
        await Browser.AddMemberAsync(factory, workspace.TenantId, djId, MembershipRole.Broadcaster);
        // A station deleted earlier: its history is kept until the workspace goes.
        await Browser.SetPlanAsync(factory, workspace.TenantId, Plan.Growth.Id);
        var old = await workspace.Owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Old", slug = "old" });
        old.Status(HttpStatusCode.Created);
        var oldId = (await old.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!.Id;
        (await workspace.Owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{oldId}")).Status(HttpStatusCode.NoContent);
        var ownerEmail = (await workspace.Owner.Http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/auth/me", Token))
            .GetProperty("email").GetString();

        var response = await DeleteAsync(workspace.Owner, $" {workspace.Slug} ");

        response.Status(HttpStatusCode.OK);
        var deleted = (await response.Content.ReadFromJsonAsync<WorkspaceDeletedResponse>(Browser.Json, Token))!;
        Assert.Equal((2, 1, 1, 2), (deleted.Counts.Stations, deleted.Counts.Tracks, deleted.Counts.Playlists, deleted.Counts.Members));
        Assert.False(await TenantExistsAsync(workspace.TenantId));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Guid[] stations = [workspace.Station.Id, oldId];
            Assert.False(await db.Stations.IgnoreQueryFilters().AnyAsync(s => stations.Contains(s.Id), Token));
            Assert.False(await db.Tracks.IgnoreQueryFilters().AnyAsync(t => stations.Contains(t.StationId), Token));
            Assert.False(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => stations.Contains(p.StationId), Token));
            Assert.False(await db.BroadcastCredentials.IgnoreQueryFilters().AnyAsync(c => stations.Contains(c.StationId), Token));
            Assert.False(await db.LiveSessions.IgnoreQueryFilters().AnyAsync(l => stations.Contains(l.StationId), Token));
            Assert.False(await db.StationStatsRollups.IgnoreQueryFilters().AnyAsync(r => stations.Contains(r.StationId), Token));
            Assert.False(await db.StreamAssignments.IgnoreQueryFilters().AnyAsync(a => stations.Contains(a.StationId), Token));
            Assert.False(await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == workspace.TenantId, Token));
            Assert.False(await db.AuditEntries.IgnoreQueryFilters().AnyAsync(e => e.TenantId == workspace.TenantId, Token));
            Assert.True(await db.Users.AnyAsync(u => u.Id == djId, Token));
            Assert.Contains(await db.SecurityEvents.Where(e => e.Action == TenantEndpoints.WorkspaceDeleted).Select(e => e.Detail).ToListAsync(Token),
                d => d!.StartsWith(workspace.TenantId.ToString(), StringComparison.Ordinal));
            // Only the StationDeleted events wait in the outbox: the rest would point at rows that are gone.
            var pending = (await db.OutboxMessages.Where(m => m.ProcessedAt == null).Select(m => new { m.Type, m.Payload }).ToListAsync(Token))
                .Where(m => m.Payload.Contains(workspace.TenantId.ToString(), StringComparison.Ordinal)).Select(m => m.Type).ToList();
            Assert.All(pending, type => Assert.Equal(nameof(StationDeleted), type));
            Assert.NotEmpty(pending);
        }
        Assert.Contains(factory.Emails.Sent, m => m.To == ownerEmail && m.Subject.Contains("was deleted", StringComparison.Ordinal));

        // The audio goes from object storage once the events are delivered.
        while (await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(Token) > 0)
        {
        }
        Assert.DoesNotContain(workspace.ObjectKey, factory.Storage.Keys);
        // The owner can no longer reach it.
        (await workspace.Owner.SendAsync(HttpMethod.Get, "/api/v1/tenants/current")).Status(HttpStatusCode.Forbidden);
    }
}
