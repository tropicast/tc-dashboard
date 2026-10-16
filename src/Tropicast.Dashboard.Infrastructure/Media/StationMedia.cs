using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Media;

/// <summary>The media rows of deleted stations.</summary>
public static class StationMedia
{
    /// <summary>
    /// Deletes a soft-deleted station's pinned encodings, AutoDJ settings, playlists (their entries cascade) and
    /// tracks. Pending uploads still in time stay: <see cref="PendingUploadCleanup"/> takes them once expired.
    /// </summary>
    public static async Task DeleteAsync(AppDbContext db, IReadOnlyCollection<Guid> stationIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string[] all = [AppDbContext.TenantFilter];
        await db.EncodingProfiles.IgnoreQueryFilters(all).Where(e => stationIds.Contains(e.StationId)).ExecuteDeleteAsync(cancellationToken);
        await db.AutoDjSettings.IgnoreQueryFilters(all).Where(a => stationIds.Contains(a.StationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Playlists.IgnoreQueryFilters(all).Where(p => stationIds.Contains(p.StationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Tracks.IgnoreQueryFilters(all)
            .Where(t => stationIds.Contains(t.StationId) && (t.Status == TrackStatus.Ready || t.UploadExpiresAt < now))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Before the stations' rows go for good (workspace deletion): uploads still in time become
    /// <see cref="OrphanedUpload"/>s, so the file a late PUT may still write is deleted once the URL expired.
    /// </summary>
    public static async Task OrphanPendingAsync(AppDbContext db, IReadOnlyCollection<Guid> stationIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await db.Tracks.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => stationIds.Contains(t.StationId) && t.Status == TrackStatus.Pending && t.UploadExpiresAt >= now)
            .Select(t => new { t.ObjectKey, t.UploadExpiresAt }).ToListAsync(cancellationToken);
        db.OrphanedUploads.AddRange(pending.Select(t => OrphanedUpload.Of(t.ObjectKey, t.UploadExpiresAt!.Value)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
