using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Tests;

public sealed class PersistenceTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_produce_the_current_model_and_seed_only_the_plan_catalogue()
    {
        await using var context = db.CreateContext(null);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Token));
        Assert.False(context.Database.HasPendingModelChanges(), "The model changed without a migration: run 'dotnet ef migrations add'.");
        var plans = await context.Plans.OrderBy(p => p.SortOrder).ToListAsync(Token);
        Assert.Equal(Plan.Catalogue.Select(p => (p.Id, p.MaxListeners, p.MaxBitrateKbps, p.Formats)),
            plans.Select(p => (p.Id, p.MaxListeners, p.MaxBitrateKbps, p.Formats)));
    }

    [Fact]
    public async Task Tenant_A_cannot_load_tenant_B_station_or_its_data_through_the_default_query_path()
    {
        var (a, stationA) = await CreateTenantWithStationAsync();
        var (b, stationB) = await CreateTenantWithStationAsync();
        await using (var setup = db.CreateContext(b.Id))
        {
            setup.BroadcastCredentials.Add(BroadcastCredential.Create(stationB.Id, "Studio PC", new string('b', 64), Now));
            setup.LiveSessions.Add(LiveSession.Start(stationB.Id, AudioFormat.Mp3, Now));
            await setup.SaveChangesAsync(Token);
        }

        await using var context = db.CreateContext(a.Id);
        Assert.Null(await context.Stations.FirstOrDefaultAsync(s => s.Id == stationB.Id, Token));
        Assert.Null(await context.Stations.FirstOrDefaultAsync(s => s.PublicId == stationB.PublicId, Token));
        Assert.Contains(await context.Stations.Select(s => s.Id).ToListAsync(Token), id => id == stationA.Id);
        Assert.DoesNotContain(await context.Stations.Select(s => s.TenantId).ToListAsync(Token), id => id != a.Id);
        Assert.False(await context.BroadcastCredentials.AnyAsync(c => c.StationId == stationB.Id, Token));
        Assert.False(await context.LiveSessions.AnyAsync(s => s.StationId == stationB.Id, Token));
        Assert.False(await context.Memberships.AnyAsync(m => m.TenantId == b.Id, Token));
        Assert.Null(await context.Tenants.FirstOrDefaultAsync(t => t.Id == b.Id, Token));
        Assert.Equal([a.Id], await context.Tenants.Select(t => t.Id).ToListAsync(Token));
    }

    [Fact]
    public async Task Without_a_tenant_the_default_query_path_returns_no_tenant_data()
    {
        await CreateTenantWithStationAsync();
        await using var context = db.CreateContext(null);
        Assert.False(await context.Stations.AnyAsync(Token));
        Assert.False(await context.Tenants.AnyAsync(Token));
        Assert.True(await context.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter]).AnyAsync(Token));
    }

    [Fact]
    public async Task Removing_a_station_keeps_its_history_and_frees_only_its_slug()
    {
        var (tenant, station) = await CreateTenantWithStationAsync();
        await using (var setup = db.CreateContext(tenant.Id))
        {
            var other = Station.Create(tenant.Id, "Other", "other", Now);
            setup.Stations.Add(other);
            setup.StreamAssignments.Add(StreamAssignment.Create(station, "tc-stream-1", Now));
            setup.LiveSessions.Add(LiveSession.Start(station.Id, AudioFormat.Opus, Now));
            setup.StationStatsRollups.Add(StationStatsRollup.Create(station.Id, RollupInterval.Day, Now, 12, 3.5, 1_000));
            await setup.SaveChangesAsync(Token);
        }

        await using (var context = db.CreateContext(tenant.Id))
        {
            var loaded = await context.Stations.SingleAsync(s => s.Id == station.Id, Token);
            loaded.Delete(Now.AddDays(1));
            await context.SaveChangesAsync(Token);
        }

        await using (var context = db.CreateContext(tenant.Id))
        {
            Assert.False(await context.Stations.AnyAsync(s => s.Id == station.Id, Token));
            Assert.True(await context.Stations.AnyAsync(s => s.Slug == "other", Token));
            var history = context.Stations.IgnoreQueryFilters([AppDbContext.DeletedFilter]);
            var deleted = await history.SingleAsync(s => s.Id == station.Id, Token);
            Assert.Equal(Now.AddDays(1), deleted.DeletedAt);
            // Rows that point at the station are still stored.
            var sessions = context.LiveSessions.IgnoreQueryFilters([AppDbContext.DeletedFilter]);
            Assert.True(await sessions.AnyAsync(s => s.StationId == station.Id, Token));
            Assert.True(await context.StationStatsRollups.IgnoreQueryFilters([AppDbContext.DeletedFilter])
                .AnyAsync(r => r.StationId == station.Id, Token));
            Assert.True(await context.StreamAssignments.IgnoreQueryFilters([AppDbContext.DeletedFilter])
                .AnyAsync(r => r.StationId == station.Id, Token));

            // The slug is free again, the public ID is not reused.
            var replacement = Station.Create(tenant.Id, "Replacement", station.Slug, Now.AddDays(2));
            context.Stations.Add(replacement);
            await context.SaveChangesAsync(Token);
            Assert.NotEqual(station.PublicId, replacement.PublicId);
        }
    }

    [Fact]
    public async Task Duplicate_slug_in_a_tenant_is_rejected()
    {
        var (tenant, station) = await CreateTenantWithStationAsync();
        await using var context = db.CreateContext(tenant.Id);
        context.Stations.Add(Station.Create(tenant.Id, "Copy", station.Slug, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Concurrent_updates_are_detected_with_xmin()
    {
        var (tenant, station) = await CreateTenantWithStationAsync();
        await using var first = db.CreateContext(tenant.Id);
        await using var second = db.CreateContext(tenant.Id);
        var a = await first.Stations.SingleAsync(s => s.Id == station.Id, Token);
        var b = await second.Stations.SingleAsync(s => s.Id == station.Id, Token);
        a.Rename("First", "first");
        await first.SaveChangesAsync(Token);
        b.Rename("Second", "second");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Public_id_cannot_change_once_saved()
    {
        var (tenant, station) = await CreateTenantWithStationAsync();
        await using var context = db.CreateContext(tenant.Id);
        var loaded = await context.Stations.SingleAsync(s => s.Id == station.Id, Token);
        context.Entry(loaded).Property(s => s.PublicId).CurrentValue = StationPublicId.New();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Timestamps_round_trip_as_utc()
    {
        var (tenant, station) = await CreateTenantWithStationAsync();
        await using var context = db.CreateContext(tenant.Id);
        var loaded = await context.Stations.SingleAsync(s => s.Id == station.Id, Token);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
    }

    private async Task<(Tenant Tenant, Station Station)> CreateTenantWithStationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var tenant = Tenant.Create($"Tenant {suffix}", $"tenant-{suffix}", Now);
        var station = Station.Create(tenant.Id, "Radio", $"radio-{suffix}", Now);
        await using var context = db.CreateContext(tenant.Id);
        var user = new AppUser { UserName = $"{suffix}@example.test", Email = $"{suffix}@example.test", CreatedAt = Now };
        context.Users.Add(user);
        context.Tenants.Add(tenant);
        context.Memberships.Add(Membership.Create(tenant.Id, user.Id, MembershipRole.Owner, Now));
        context.Stations.Add(station);
        await context.SaveChangesAsync(Token);
        return (tenant, station);
    }
}
