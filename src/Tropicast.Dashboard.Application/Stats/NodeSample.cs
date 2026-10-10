using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Application.Stats;

/// <summary>One live mount on a streaming node at sample time.</summary>
/// <param name="PublicId">Station public ID, from the mount <c>/stations/{id}/live.{fmt}</c>.</param>
/// <param name="Format">Stream format.</param>
/// <param name="Listeners">Current listeners.</param>
/// <param name="BitrateKbps">Source bitrate (declared or measured); 0 when unknown yet.</param>
/// <param name="SentBytes">Bytes sent to listeners since the source connected.</param>
/// <param name="StartedAt">When the source connected; null when unknown.</param>
public sealed record MountSample(string PublicId, AudioFormat Format, int Listeners, double BitrateKbps, long SentBytes,
    DateTimeOffset? StartedAt);

/// <summary>What a streaming node reported at <see cref="At"/>.</summary>
/// <param name="Node">Node name.</param>
/// <param name="At">When the sample was taken.</param>
/// <param name="IcecastUp">False when the exporter could not read Icecast: mounts are then unknown, not offline.</param>
/// <param name="Mounts">Live station mounts.</param>
/// <param name="EgressBytes">Node outgoing traffic in the current billing period (Hetzner), when known.</param>
/// <param name="IncludedBytes">Traffic included in the server price, when known.</param>
public sealed record NodeSample(string Node, DateTimeOffset At, bool IcecastUp, IReadOnlyList<MountSample> Mounts,
    long? EgressBytes, long? IncludedBytes);

/// <summary>Reads a streaming node's stats (tc-streaming exporter, private network).</summary>
public interface IStreamingStatsClient
{
    /// <summary>The node's current stats. Throws when the node cannot be reached.</summary>
    Task<NodeSample> GetAsync(string node, CancellationToken cancellationToken = default);
}
