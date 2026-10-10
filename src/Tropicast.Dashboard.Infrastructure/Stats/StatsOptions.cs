namespace Tropicast.Dashboard.Infrastructure.Stats;

/// <summary>Live status and listener statistics (<c>Stats</c> section).</summary>
public sealed class StatsOptions
{
    /// <summary>Collect at all. Without <see cref="Url"/> the collector stays idle.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// The streaming node's exporter, over the private network, e.g. <c>http://10.20.1.2:9100/metrics</c>.
    /// It needs no credential: Icecast's admin password stays on the node.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>The exporter address, or null when <see cref="Url"/> is empty or not an absolute HTTP(S) URL.</summary>
    public Uri? ExporterUrl => Uri.TryCreate(Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" ? url : null;
    /// <summary>Node the exporter belongs to; the API sets it from <c>Streaming:Node</c>.</summary>
    public string Node { get; set; } = "tc-stream-1";
    /// <summary>How often to sample. A station shows live within this delay of Go Live.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gaps longer than this (collector or node down) add no listener time.</summary>
    public TimeSpan MaxGap { get; set; } = TimeSpan.FromSeconds(90);
    /// <summary>Egress per audio byte (tc-streaming docs/load-test.md, #37).</summary>
    public double OverheadFactor { get; set; } = Application.Stats.ListenerAccounting.DefaultOverheadFactor;
    /// <summary>5-minute rollups are kept this long; daily rollups forever.</summary>
    public TimeSpan FiveMinuteRetention { get; set; } = TimeSpan.FromDays(30);
}
