using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>A station's live state from the latest node sample (stats collector).</summary>
internal static class LiveStatus
{
    /// <summary>
    /// Live is null when the state is not known: no recent sample (three missed: the node or the collector is down),
    /// or Icecast did not answer. Live means a live source is connected; when it drops, Icecast moves the listeners to
    /// AutoDJ's fallback mount, so each output counts the listeners of both mounts of its format.
    /// The bitrate and start time are the live source's: AutoDJ outputs show neither, even when the node reports them.
    /// </summary>
    internal static (bool? Live, IReadOnlyList<LiveOutputResponse> Outputs, DateTimeOffset? UpdatedAt) Of(LiveStatusStore store,
        string node, string publicId, TimeSpan interval, DateTimeOffset now)
    {
        var sample = store.Latest(node);
        if (sample is null || !sample.IcecastUp || now - sample.At > 3 * interval)
        {
            return (null, [], sample?.At);
        }
        var outputs = sample.Mounts
            .Where(m => m.PublicId == publicId)
            .GroupBy(m => m.Format)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var live = g.FirstOrDefault(m => !m.AutoDj);
                return new LiveOutputResponse(g.Key, g.Sum(m => m.Listeners), live?.BitrateKbps > 0 ? live.BitrateKbps : null,
                    live?.StartedAt, live is null);
            })
            .ToList();
        return (outputs.Any(o => !o.AutoDj), outputs, sample.At);
    }
}
