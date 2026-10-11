using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Application.Directories;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>Directory entries of stations, across tenants (provisioning and the listing worker).</summary>
public static class DirectoryEntries
{
    public static async Task<Dictionary<Guid, DirectoryEntry>> ForAsync(AppDbContext db, DirectoryOptions options,
        IReadOnlyCollection<Guid> stationIds, CancellationToken cancellationToken)
    {
        var rows = await db.Stations.IgnoreQueryFilters()
            .Where(s => stationIds.Contains(s.Id))
            .Join(db.Tenants.IgnoreQueryFilters(), s => s.TenantId, t => t.Id, (s, t) => new { Station = s, t.Slug, t.PlanId, t.Status })
            .Join(db.Plans, x => x.PlanId, p => p.Id, (x, p) => new { x.Station, x.Slug, x.Status, Plan = p })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var submitted = await db.DirectoryListings
            .Where(l => stationIds.Contains(l.StationId) && l.RadioBrowserUuid != null)
            .Select(l => l.StationId)
            .ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, DirectoryEntry>();
        foreach (var row in rows)
        {
            var station = row.Station;
            // A suspended tenant's station is not listed.
            var effective = row.Status == Domain.Tenants.TenantStatus.Active ? row.Plan : Domain.Plans.Plan.Free;
            var entry = DirectoryEntry.For(station, effective, submitted.Contains(station.Id),
                new Uri(options.ListenerBaseUrl, $"/stations/{station.PublicId}/live.mp3"),
                new Uri(options.PublicBaseUrl, $"/listen/{row.Slug}/{station.Slug}"));
            if (entry is not null)
            {
                result[station.Id] = entry;
            }
        }
        return result;
    }
}
