using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Domain.Tests;

public sealed class PlanTests
{
    [Fact]
    public void Catalogue_is_ordered_and_each_plan_raises_the_limits()
    {
        var plans = Plan.Catalogue;
        Assert.Equal(["free", "starter", "growth", "pro"], plans.Select(p => p.Id));
        for (var i = 1; i < plans.Count; i++)
        {
            Assert.True(plans[i].MaxListeners > plans[i - 1].MaxListeners);
            Assert.True(plans[i].MaxBitrateKbps > plans[i - 1].MaxBitrateKbps);
            Assert.True(plans[i].MaxStations >= plans[i - 1].MaxStations);
        }
    }

    [Fact]
    public void Free_plan_matches_the_streaming_node_default_cap()
    {
        // tc-streaming stations.json: "default": {"max_listeners": 100}, free plan max_bitrate_kbps 64.
        Assert.Equal(100, Plan.Free.MaxListeners);
        Assert.Equal(64, Plan.Free.MaxBitrateKbps);
        Assert.True(Plan.Free.Allows(AudioFormat.Mp3));
        Assert.True(Plan.Free.Allows(AudioFormat.Opus));
    }
}
