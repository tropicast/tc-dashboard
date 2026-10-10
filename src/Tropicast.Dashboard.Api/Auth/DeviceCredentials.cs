using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>Broadcast credentials the desktop app got for itself end with its device session.</summary>
internal static class DeviceCredentials
{
    /// <summary>Revokes the active broadcast credentials of these device sessions, in every tenant.</summary>
    internal static Task<int> RevokeAsync(AppDbContext db, IQueryable<DeviceSession> sessions, DateTimeOffset now,
        CancellationToken cancellationToken)
        => db.BroadcastCredentials.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(c => c.RevokedAt == null && sessions.Select(s => (Guid?)s.Id).Contains(c.DeviceSessionId))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.RevokedAt, now), cancellationToken);
}
