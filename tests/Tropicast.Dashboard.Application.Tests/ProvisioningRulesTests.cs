using System.Text.Json;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Tests;

public sealed class ProvisioningRulesTests
{
    [Fact]
    public void Stations_json_matches_the_streaming_node_schema()
    {
        var rendered = StationLimitsDocument.Render([new("k3m9x2p7qa", Plan.Free), new("a1b2c3d4e5", Plan.Growth)], 100);
        using var json = JsonDocument.Parse(rendered.Json);
        Assert.Equal(100, json.RootElement.GetProperty("default").GetProperty("max_listeners").GetInt32());
        var free = json.RootElement.GetProperty("stations").GetProperty("k3m9x2p7qa");
        Assert.Equal("free", free.GetProperty("plan").GetString());
        Assert.Equal((100, 64), (free.GetProperty("max_listeners").GetInt32(), free.GetProperty("max_bitrate_kbps").GetInt32()));
        Assert.Equal(["mp3", "opus"], free.GetProperty("formats").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(5000, json.RootElement.GetProperty("stations").GetProperty("a1b2c3d4e5").GetProperty("max_listeners").GetInt32());
    }

    [Fact]
    public void Rendering_is_deterministic_and_versions_follow_content()
    {
        var a = StationLimitsDocument.Render([new("b", Plan.Free), new("a", Plan.Starter)], 100);
        var b = StationLimitsDocument.Render([new("a", Plan.Starter), new("b", Plan.Free)], 100);
        var c = StationLimitsDocument.Render([new("a", Plan.Growth), new("b", Plan.Free)], 100);
        Assert.Equal(a, b);
        Assert.NotEqual(a.Version, c.Version);
        Assert.Equal(16, a.Version.Length);
        Assert.True(a.Json.IndexOf("\"a\"", StringComparison.Ordinal) < a.Json.IndexOf("\"b\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_node_refuses_stations_beyond_its_sources_or_listener_caps()
    {
        var limits = new NodeLimits(MaxSources: 4, MaxListeners: 700);
        Assert.Null(NodeCapacity.WhyNot([Plan.Free], Plan.Starter, limits));
        Assert.Contains("sources", NodeCapacity.WhyNot([Plan.Free, Plan.Free], Plan.Free, limits), StringComparison.Ordinal);
        Assert.Contains("listener caps", NodeCapacity.WhyNot([Plan.Starter], Plan.Starter, limits), StringComparison.Ordinal);
    }
}
