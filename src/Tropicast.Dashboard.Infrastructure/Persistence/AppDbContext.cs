using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

/// <summary>
/// The control-plane database. Two named query filters guard the default query path:
/// <see cref="TenantFilter"/> limits rows to the current tenant, <see cref="DeletedFilter"/> hides deleted stations.
/// Use <c>IgnoreQueryFilters([...])</c> with the filter name only where crossing them is intended.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public const string TenantFilter = "tenant";
    public const string DeletedFilter = "deleted";

    /// <summary>Read per query, so one context serves one tenant scope at a time.</summary>
    private Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StreamAssignment> StreamAssignments => Set<StreamAssignment>();
    public DbSet<BroadcastCredential> BroadcastCredentials => Set<BroadcastCredential>();
    public DbSet<LiveSession> LiveSessions => Set<LiveSession>();
    public DbSet<StationStatsRollup> StationStatsRollups => Set<StationStatsRollup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // A user's tenants are listed through memberships with IgnoreQueryFilters([TenantFilter]).
        modelBuilder.Entity<Tenant>().HasQueryFilter(TenantFilter, e => e.Id == CurrentTenantId);
        modelBuilder.Entity<Membership>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Subscription>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Station>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Station>().HasQueryFilter(DeletedFilter, e => e.DeletedAt == null);
        // Station data reaches the tenant through its station; a deleted station's rows stay in the database.
        modelBuilder.Entity<StreamAssignment>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        modelBuilder.Entity<BroadcastCredential>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        modelBuilder.Entity<LiveSession>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        modelBuilder.Entity<StationStatsRollup>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Optimistic concurrency on PostgreSQL's xmin system column.
            modelBuilder.Entity(entity.ClrType).Property<uint>("Version").IsRowVersion();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name, so reordering or inserting members never changes stored data.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
