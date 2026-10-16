using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Media;

/// <summary>The media rows of deleted stations: one purge for station deletion and workspace deletion.</summary>
public static class StationMedia
{
    /// <summary>
    /// Deletes the stations' pinned encodings, AutoDJ settings, playlists (their entries cascade) and tracks. Pending
    /// uploads still in time are kept, or with <paramref name="orphanPending"/> moved to <see cref="OrphanedUpload"/>
    /// so their rows can go with the station.
    /// </summary>
    /// <returns>Tracks and playlists deleted.</returns>
    public static async Task<(int Tracks, int Playlists)> DeleteAsync(AppDbContext db, IReadOnlyCollection<Guid> stationIds,
        DateTimeOffset now, bool orphanPending, CancellationToken cancellationToken)
    {
        string[] all = [AppDbContext.TenantFilter];
        await db.EncodingProfiles.IgnoreQueryFilters(all).Where(e => stationIds.Contains(e.StationId)).ExecuteDeleteAsync(cancellationToken);
        await db.AutoDjSettings.IgnoreQueryFilters(all).Where(a => stationIds.Contains(a.StationId)).ExecuteDeleteAsync(cancellationToken);
        var playlists = await db.Playlists.IgnoreQueryFilters(all).Where(p => stationIds.Contains(p.StationId))
            .ExecuteDeleteAsync(cancellationToken);
        var tracks = db.Tracks.IgnoreQueryFilters(all).Where(t => stationIds.Contains(t.StationId));
        var inTime = tracks.Where(t => t.Status == TrackStatus.Pending && t.UploadExpiresAt >= now);
        if (orphanPending)
        {
            foreach (var pending in await inTime.Select(t => new { t.ObjectKey, t.UploadExpiresAt }).ToListAsync(cancellationToken))
            {
                db.OrphanedUploads.Add(OrphanedUpload.Of(pending.ObjectKey, pending.UploadExpiresAt!.Value));
            }
            await db.SaveChangesAsync(cancellationToken);
            return (await tracks.ExecuteDeleteAsync(cancellationToken), playlists);
        }
        return (await tracks.Where(t => t.Status == TrackStatus.Ready || t.UploadExpiresAt < now).ExecuteDeleteAsync(cancellationToken),
            playlists);
    }
}
