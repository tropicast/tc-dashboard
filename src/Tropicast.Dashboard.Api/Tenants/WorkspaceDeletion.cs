using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Media;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

/// <summary>Why a workspace cannot be deleted now.</summary>
internal enum DeletionBlocker
{
    /// <summary>A subscription Stripe may still charge for (<see cref="Subscription.MayCharge"/>): cancel it first.</summary>
    Subscription,
    /// <summary>A station is live: stop the broadcast first.</summary>
    Live,
}

/// <summary>A member of the workspace and what deleting it leaves them.</summary>
/// <param name="Email">Member's email.</param>
/// <param name="Role">Role in this workspace.</param>
/// <param name="LeftWithoutWorkspace">The account stays, with no workspace left.</param>
internal sealed record DeletionMemberResponse(string Email, MembershipRole Role, bool LeftWithoutWorkspace);

/// <summary>A station of the workspace.</summary>
/// <param name="PublicId">Public ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Deleted">Already deleted (its history stayed until now).</param>
/// <param name="Live">A DJ is live now: blocks the deletion.</param>
/// <param name="ListedOnRadioBrowser">Its RadioBrowser entry stays there and goes offline.</param>
internal sealed record DeletionStationResponse(string PublicId, string Name, bool Deleted, bool Live, bool ListedOnRadioBrowser);

/// <summary>Rows deleted with the workspace, per kind.</summary>
internal sealed record DeletionCountsResponse(int Stations, int Credentials, int LiveSessions, int StatsRollups, int Tracks,
    int Playlists, int Members, int Invitations, int AuditEntries);

/// <summary>What deleting the current workspace would do (nothing changes).</summary>
/// <param name="Id">Workspace ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Slug">Slug: type it to confirm the deletion.</param>
/// <param name="PlanId">Current plan.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="SubscriptionStatus">The subscription's status, or null when there is none.</param>
/// <param name="Members">Members; their accounts stay.</param>
/// <param name="Stations">Stations, deleted ones too.</param>
/// <param name="Counts">What goes.</param>
/// <param name="Blockers">Why it cannot be deleted now; empty when it can.</param>
internal sealed record WorkspaceDeletionResponse(Guid Id, string Name, string Slug, string PlanId, DateTimeOffset CreatedAt,
    SubscriptionStatus? SubscriptionStatus, IReadOnlyList<DeletionMemberResponse> Members,
    IReadOnlyList<DeletionStationResponse> Stations, DeletionCountsResponse Counts, IReadOnlyList<DeletionBlocker> Blockers);

/// <summary>
/// Deleting a whole workspace (#125), by its Owner: the same steps as the operator's <c>ops/delete-workspace.sh</c>.
/// <see cref="ReportAsync"/> says what goes and what blocks it; <see cref="DeleteAsync"/> checks the blockers again
/// with the workspace row locked, then deletes everything in one transaction. Member accounts stay. Uploaded audio
/// and the streaming node's mounts go after the commit, from the <see cref="StationDeleted"/> events.
/// </summary>
internal static class WorkspaceDeletion
{
    private static readonly string[] All = [AppDbContext.TenantFilter, AppDbContext.DeletedFilter];

    internal static async Task<WorkspaceDeletionResponse> ReportAsync(AppDbContext db, Tenant tenant, CancellationToken cancellationToken)
    {
        var stations = await AllStations(db, tenant.Id).OrderBy(s => s.CreatedAt)
            .Select(s => new DeletionStationResponse(s.PublicId, s.Name, s.DeletedAt != null,
                db.LiveSessions.IgnoreQueryFilters(All).Any(l => l.StationId == s.Id && l.EndedAt == null),
                db.DirectoryListings.Any(l => l.StationId == s.Id && l.RadioBrowserUuid != null)))
            .ToListAsync(cancellationToken);
        var members = await (
                from m in db.Memberships.IgnoreQueryFilters(All)
                join u in db.Users on m.UserId equals u.Id
                where m.TenantId == tenant.Id
                orderby m.Role, u.Email
                select new DeletionMemberResponse(u.Email!, m.Role,
                    !db.Memberships.IgnoreQueryFilters(All).Any(o => o.UserId == m.UserId && o.TenantId != tenant.Id)))
            .ToListAsync(cancellationToken);
        var subscription = await db.Subscriptions.IgnoreQueryFilters(All).Where(s => s.TenantId == tenant.Id)
            .Select(s => (SubscriptionStatus?)s.Status).FirstOrDefaultAsync(cancellationToken);
        return new WorkspaceDeletionResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.PlanId, tenant.CreatedAt, subscription,
            members, stations, await CountsAsync(db, tenant.Id, cancellationToken),
            Blockers(subscription, stations.Any(s => s.Live)));
    }

    /// <summary>
    /// Deletes the workspace, or returns its blockers and changes nothing; <c>Gone</c> when another request deleted it
    /// first. Call inside a transaction: the locks hold until the commit. The workspace row lock stops new stations,
    /// subscriptions and members; the station row locks stop what hangs off a station (a DJ connecting, an upload, a
    /// credential), whose foreign key check needs a share lock on its station.
    /// </summary>
    internal static async Task<(bool Gone, IReadOnlyList<DeletionBlocker> Blockers, DeletionCountsResponse? Counts)> DeleteAsync(
        AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {tenantId} FOR UPDATE")
                .IgnoreQueryFilters(All).AsNoTracking().SingleOrDefaultAsync(cancellationToken) is null)
        {
            return (true, [], null);
        }
        var stations = await db.Stations.FromSql($"SELECT *, xmin FROM stations WHERE tenant_id = {tenantId} ORDER BY id FOR UPDATE")
            .IgnoreQueryFilters(All).ToListAsync(cancellationToken);
        List<Guid> stationIds = [.. stations.Select(s => s.Id)];
        var subscription = await db.Subscriptions.IgnoreQueryFilters(All).Where(s => s.TenantId == tenantId)
            .Select(s => (SubscriptionStatus?)s.Status).FirstOrDefaultAsync(cancellationToken);
        var live = await db.LiveSessions.IgnoreQueryFilters(All).AnyAsync(l => stationIds.Contains(l.StationId) && l.EndedAt == null,
            cancellationToken);
        if (Blockers(subscription, live) is { Count: > 0 } blockers)
        {
            return (false, blockers, null);
        }

        // Events not delivered yet would point at rows that no longer exist: the workspace's, and its stations' (AutoDJ
        // changes carry only the station). StationDeleted ones stay: their consumers still have audio to remove.
        var id = tenantId.ToString();
        string[] stationKeys = [.. stationIds.Select(s => s.ToString())];
        await db.Database.ExecuteSqlAsync($"""
            DELETE FROM outbox_messages WHERE processed_at IS NULL AND type <> 'StationDeleted'
              AND (payload ->> 'tenantId' = {id} OR payload ->> 'stationId' = ANY({stationKeys}))
            """, cancellationToken);
        // One StationDeleted per station still on air: the node drops its mounts and its audio leaves object storage.
        foreach (var station in stations)
        {
            station.Delete(now);
        }
        await db.SaveChangesAsync(cancellationToken);

        // What cascades with the workspace is counted first; the rest comes from the deletes.
        var members = await db.Memberships.IgnoreQueryFilters(All).CountAsync(m => m.TenantId == tenantId, cancellationToken);
        var invitations = await db.Invitations.IgnoreQueryFilters(All).CountAsync(i => i.TenantId == tenantId, cancellationToken);
        var audit = await db.AuditEntries.IgnoreQueryFilters(All).CountAsync(e => e.TenantId == tenantId, cancellationToken);
        // Station children are RESTRICT: they go first. Pending uploads become orphans the upload cleanup deletes.
        var credentials = await db.BroadcastCredentials.IgnoreQueryFilters(All).Where(c => stationIds.Contains(c.StationId))
            .ExecuteDeleteAsync(cancellationToken);
        var sessions = await db.LiveSessions.IgnoreQueryFilters(All).Where(l => stationIds.Contains(l.StationId))
            .ExecuteDeleteAsync(cancellationToken);
        var rollups = await db.StationStatsRollups.IgnoreQueryFilters(All).Where(r => stationIds.Contains(r.StationId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.StreamAssignments.IgnoreQueryFilters(All).Where(a => stationIds.Contains(a.StationId)).ExecuteDeleteAsync(cancellationToken);
        await db.DirectoryListings.Where(l => stationIds.Contains(l.StationId)).ExecuteDeleteAsync(cancellationToken);
        var (tracks, playlists) = await StationMedia.DeleteAsync(db, stationIds, now, orphanPending: true, cancellationToken);
        var deleted = await db.Stations.IgnoreQueryFilters(All).Where(s => s.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
        // Memberships, subscriptions, invitations and audit entries cascade with the workspace.
        await db.Tenants.IgnoreQueryFilters(All).Where(t => t.Id == tenantId).ExecuteDeleteAsync(cancellationToken);
        return (false, [], new DeletionCountsResponse(deleted, credentials, sessions, rollups, tracks, playlists, members, invitations, audit));
    }

    private static List<DeletionBlocker> Blockers(SubscriptionStatus? subscription, bool live)
    {
        List<DeletionBlocker> blockers = [];
        if (subscription is { } status && Subscription.MayCharge(status))
        {
            blockers.Add(DeletionBlocker.Subscription);
        }
        if (live)
        {
            blockers.Add(DeletionBlocker.Live);
        }
        return blockers;
    }

    private static async Task<DeletionCountsResponse> CountsAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var stations = AllStations(db, tenantId).Select(s => s.Id);
        return new DeletionCountsResponse(
            await stations.CountAsync(cancellationToken),
            await db.BroadcastCredentials.IgnoreQueryFilters(All).CountAsync(c => stations.Contains(c.StationId), cancellationToken),
            await db.LiveSessions.IgnoreQueryFilters(All).CountAsync(l => stations.Contains(l.StationId), cancellationToken),
            await db.StationStatsRollups.IgnoreQueryFilters(All).CountAsync(r => stations.Contains(r.StationId), cancellationToken),
            await db.Tracks.IgnoreQueryFilters(All).CountAsync(t => stations.Contains(t.StationId), cancellationToken),
            await db.Playlists.IgnoreQueryFilters(All).CountAsync(p => stations.Contains(p.StationId), cancellationToken),
            await db.Memberships.IgnoreQueryFilters(All).CountAsync(m => m.TenantId == tenantId, cancellationToken),
            await db.Invitations.IgnoreQueryFilters(All).CountAsync(i => i.TenantId == tenantId, cancellationToken),
            await db.AuditEntries.IgnoreQueryFilters(All).CountAsync(e => e.TenantId == tenantId, cancellationToken));
    }

    /// <summary>The workspace's stations, deleted ones too.</summary>
    private static IQueryable<Station> AllStations(AppDbContext db, Guid tenantId)
        => db.Stations.IgnoreQueryFilters(All).Where(s => s.TenantId == tenantId);

}
