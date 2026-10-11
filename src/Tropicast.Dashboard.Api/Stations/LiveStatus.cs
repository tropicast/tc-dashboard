using Tropicast.Dashboard.Infrastructure.Stats;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>A station's live state from the latest node sample (stats collector).</summary>
internal static class LiveStatus
{
    /// <summary>
    /// Live is null when the state is not known: no recent sample (three missed: the node or the collector is down),
    /// or Icecast did not answer.
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
            .OrderBy(m => m.Format)
            .Select(m => new LiveOutputResponse(m.Format, m.Listeners, m.BitrateKbps > 0 ? m.BitrateKbps : null, m.StartedAt))
            .ToList();
        return (outputs.Count > 0, outputs, sample.At);
    }
}
