using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Provisioning;

/// <summary>What one streaming node can carry (Icecast <c>&lt;sources&gt;</c> and the tested listener load).</summary>
/// <param name="MaxSources">Icecast's global source limit: every allowed format of a station may be live at once.</param>
/// <param name="MaxListeners">Sum of station listener caps the node accepts (oversubscribed: Icecast serves at most 1,500 at once).</param>
public sealed record NodeLimits(int MaxSources = 50, int MaxListeners = 15_000);

/// <summary>Refuses to place a station on a node that its caps would overload.</summary>
public static class NodeCapacity
{
    public static int SourcesOf(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return (plan.Allows(AudioFormat.Mp3) ? 1 : 0) + (plan.Allows(AudioFormat.Opus) ? 1 : 0);
    }

    /// <summary>Null when <paramref name="adding"/> fits next to <paramref name="assigned"/>; otherwise why not.</summary>
    public static string? WhyNot(IEnumerable<Plan> assigned, Plan adding, NodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(assigned);
        ArgumentNullException.ThrowIfNull(adding);
        ArgumentNullException.ThrowIfNull(limits);
        var plans = assigned.Append(adding).ToList();
        var sources = plans.Sum(SourcesOf);
        if (sources > limits.MaxSources)
        {
            return $"the node would need {sources} sources, above its limit of {limits.MaxSources}";
        }
        var listeners = plans.Sum(p => p.MaxListeners);
        return listeners > limits.MaxListeners
            ? $"the node's listener caps would total {listeners}, above its limit of {limits.MaxListeners}"
            : null;
    }
}
