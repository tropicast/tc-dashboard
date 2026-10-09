using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Outbox;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

/// <summary>
/// The control-plane database. Two named query filters guard the default query path:
/// <see cref="TenantFilter"/> limits rows to the current tenant, <see cref="DeletedFilter"/> hides deleted stations.
/// Use <c>IgnoreQueryFilters([...])</c> with the filter name only where crossing them is intended.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant)
    : IdentityUserContext<AppUser, Guid>(options)
{
    public const string TenantFilter = "tenant";
    public const string DeletedFilter = "deleted";


    /// <summary>Read per query, so one context serves one tenant scope at a time.</summary>
    private Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StreamAssignment> StreamAssignments => Set<StreamAssignment>();
    public DbSet<BroadcastCredential> BroadcastCredentials => Set<BroadcastCredential>();
    public DbSet<LiveSession> LiveSessions => Set<LiveSession>();
    public DbSet<StationStatsRollup> StationStatsRollups => Set<StationStatsRollup>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name").IsUnique();
            // Identity enforces unique emails in code; the database backs it up against races.
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email").IsUnique();
        });
        // The passkey set exists on the base context for every schema version; schema 2 has no table for it.
        builder.Ignore<IdentityUserPasskey<Guid>>();
        builder.Ignore<IdentityPasskeyData>();
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // A user's tenants are listed through memberships with IgnoreQueryFilters([TenantFilter]).
        builder.Entity<Tenant>().HasQueryFilter(TenantFilter, e => e.Id == CurrentTenantId);
        builder.Entity<Membership>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        builder.Entity<Invitation>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        builder.Entity<Subscription>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        builder.Entity<Station>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        builder.Entity<Station>().HasQueryFilter(DeletedFilter, e => e.DeletedAt == null);
        // Station data reaches the tenant through its station; a deleted station's rows stay in the database.
        builder.Entity<StreamAssignment>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        builder.Entity<BroadcastCredential>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        builder.Entity<LiveSession>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);
        builder.Entity<StationStatsRollup>().HasQueryFilter(TenantFilter, e => e.Station.TenantId == CurrentTenantId);

        foreach (var entity in builder.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList())
        {
            // Optimistic concurrency on PostgreSQL's xmin system column.
            builder.Entity(entity.ClrType).Property<uint>("Version").IsRowVersion();
        }
    }

    /// <summary>Saves the changes and the domain events they raised, in one transaction (the outbox).</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var sources = ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        foreach (var domainEvent in sources.SelectMany(s => s.DomainEvents))
        {
            OutboxMessages.Add(OutboxMessage.From(domainEvent.GetType().Name,
                JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OutboxJson), domainEvent.OccurredAt));
        }
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        sources.ForEach(s => s.ClearDomainEvents());
        return saved;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException("Use SaveChangesAsync: it also writes the outbox.");

    private static readonly JsonSerializerOptions OutboxJson = new(JsonSerializerDefaults.Web);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name, so reordering or inserting members never changes stored data.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
