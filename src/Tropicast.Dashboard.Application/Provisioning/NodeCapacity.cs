using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Provisioning;

/// <summary>What one streaming node can carry (Icecast <c>&lt;sources&gt;</c> and the tested listener load).</summary>
/// <param name="MaxSources">Icecast's global source limit: every allowed format of a station may be live at once.</param>
/// <param name="MaxListeners">Sum of station listener caps the node accepts (oversubscribed: Icecast serves at most 1,500 at once).</param>
/// <param name="MaxListenersPerStation">
/// Icecast's <c>&lt;clients&gt;</c>: no station can draw more than this from one node, whatever its plan's cap. A station
/// counts at most this much in <paramref name="MaxListeners"/>, so a plan whose cap exceeds a whole node (Pro) still fits.
/// </param>
public sealed record NodeLimits(int MaxSources = 50, int MaxListeners = 15_000, int MaxListenersPerStation = 1_500);

/// <summary>Refuses to place stations on a node that their caps would overload.</summary>
public static class NodeCapacity
{
    public static int SourcesOf(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return (plan.Allows(AudioFormat.Mp3) ? 1 : 0) + (plan.Allows(AudioFormat.Opus) ? 1 : 0);
    }

    /// <summary>The listener load a station of this plan can put on one node: its cap, clipped to the node's ceiling.</summary>
    public static int ListenersOf(Plan plan, NodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        return Math.Min(plan.MaxListeners, limits.MaxListenersPerStation);
    }

    /// <summary>Null when <paramref name="adding"/> fits next to <paramref name="assigned"/>; otherwise why not.</summary>
    public static string? WhyNot(IEnumerable<Plan> assigned, Plan adding, NodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(assigned);
        ArgumentNullException.ThrowIfNull(adding);
        return WhyNot(assigned.Append(adding), limits);
    }

    /// <summary>Null when the node can carry one station per plan in <paramref name="stations"/>; otherwise why not.</summary>
    public static string? WhyNot(IEnumerable<Plan> stations, NodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(limits);
        var plans = stations.ToList();
        var sources = plans.Sum(SourcesOf);
        if (sources > limits.MaxSources)
        {
            return $"the node would need {sources} sources, above its limit of {limits.MaxSources}";
        }
        var listeners = plans.Sum(p => ListenersOf(p, limits));
        return listeners > limits.MaxListeners
            ? $"the node's listener caps would total {listeners}, above its limit of {limits.MaxListeners}"
            : null;
    }
}
