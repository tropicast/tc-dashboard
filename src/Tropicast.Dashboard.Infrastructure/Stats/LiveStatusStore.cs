using System.Collections.Concurrent;
using Tropicast.Dashboard.Application.Stats;

namespace Tropicast.Dashboard.Infrastructure.Stats;

/// <summary>The latest sample of each streaming node, for live status. In memory: the next sample refills it.</summary>
public sealed class LiveStatusStore
{
    private readonly ConcurrentDictionary<string, NodeSample> _latest = new(StringComparer.Ordinal);

    public NodeSample? Latest(string node) => _latest.GetValueOrDefault(node);

    internal void Set(NodeSample sample) => _latest[sample.Node] = sample;
}
