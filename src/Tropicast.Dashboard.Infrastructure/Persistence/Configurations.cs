using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tropicast.Dashboard.Domain.Audit;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Outbox;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Slug).HasMaxLength(64);
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasOne<Plan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
        builder.HasIndex(e => e.UserId);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.Property(e => e.Id).HasMaxLength(32);
        builder.Property(e => e.Name).HasMaxLength(64);
        // Stored by name like every enum ("Mp3, Opus"), so renumbering the flags never changes stored data.
        builder.Property(e => e.Formats).HasMaxLength(64);
        builder.HasData(Plan.Catalogue);
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Plan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ProviderCustomerId).HasMaxLength(255);
        builder.Property(e => e.ProviderSubscriptionId).HasMaxLength(255);
        builder.HasIndex(e => e.ProviderSubscriptionId).IsUnique();
    }
}

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.PublicId).HasMaxLength(StationPublicId.Length).IsFixedLength();
        // Part of listener URLs and Icecast mounts: never changes once saved, never reused.
        builder.Property(e => e.PublicId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasIndex(e => e.PublicId).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Slug).HasMaxLength(64);
        // A deleted station frees its slug, not its public ID.
        builder.HasIndex(e => new { e.TenantId, e.Slug }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Genre).HasMaxLength(64);
        builder.Property(e => e.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(e => e.Language).HasMaxLength(35);
        builder.Property(e => e.LogoUrl).HasMaxLength(2048);
        builder.Property(e => e.Website).HasMaxLength(2048);
        builder.Ignore(e => e.IsDeleted);
    }
}

internal sealed class StreamAssignmentConfiguration : IEntityTypeConfiguration<StreamAssignment>
{
    public void Configure(EntityTypeBuilder<StreamAssignment> builder)
    {
        builder.HasOne(e => e.Station).WithMany().HasForeignKey(e => e.StationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.StationId).IsUnique();
        builder.Property(e => e.Node).HasMaxLength(64);
        builder.Property(e => e.MountBase).HasMaxLength(64);
    }
}

internal sealed class BroadcastCredentialConfiguration : IEntityTypeConfiguration<BroadcastCredential>
{
    public void Configure(EntityTypeBuilder<BroadcastCredential> builder)
    {
        builder.HasOne(e => e.Station).WithMany().HasForeignKey(e => e.StationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.StationId);
        // One active credential per device label; revoking frees the label.
        builder.HasIndex(e => new { e.StationId, e.DeviceLabel }).IsUnique().HasFilter("revoked_at IS NULL");
        builder.Property(e => e.DeviceLabel).HasMaxLength(64);
        builder.Property(e => e.SecretHash).HasMaxLength(64).IsFixedLength();
        builder.Ignore(e => e.IsActive);
    }
}

internal sealed class LiveSessionConfiguration : IEntityTypeConfiguration<LiveSession>
{
    public void Configure(EntityTypeBuilder<LiveSession> builder)
    {
        builder.HasOne(e => e.Station).WithMany().HasForeignKey(e => e.StationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.StationId, e.StartedAt });
        // At most one open session per station and format.
        builder.HasIndex(e => new { e.StationId, e.Format }).IsUnique().HasFilter("ended_at IS NULL");
    }
}

internal sealed class StationStatsRollupConfiguration : IEntityTypeConfiguration<StationStatsRollup>
{
    public void Configure(EntityTypeBuilder<StationStatsRollup> builder)
    {
        builder.HasKey(e => new { e.StationId, e.Interval, e.PeriodStart });
        builder.HasOne(e => e.Station).WithMany().HasForeignKey(e => e.StationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.InvitedBy).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.Email).HasMaxLength(256);
        builder.Property(e => e.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Email });
    }
}

internal sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.DeviceName).HasMaxLength(64);
        // Looked up by the session ID inside the token, then compared: no index on the hash.
        builder.Property(e => e.RefreshTokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(e => e.UserId);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(e => e.Type).HasMaxLength(128);
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        builder.Property(e => e.LastError).HasMaxLength(256);
        // The dispatcher only reads unprocessed rows.
        builder.HasIndex(e => e.OccurredAt).HasFilter("processed_at IS NULL");
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.Action).HasMaxLength(64);
        builder.Property(e => e.TargetType).HasMaxLength(64);
        builder.Property(e => e.TargetId).HasMaxLength(64);
        builder.Property(e => e.Summary).HasMaxLength(256);
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
    }
}
