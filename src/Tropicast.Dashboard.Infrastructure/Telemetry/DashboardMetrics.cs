using System.Diagnostics.Metrics;

namespace Tropicast.Dashboard.Infrastructure.Telemetry;

/// <summary>
/// The control plane's own metrics (meter <c>Tropicast.Dashboard</c>), exported with OpenTelemetry. Alert rules in
/// <c>monitoring/rules.yaml</c> use their Prometheus names (dots become underscores, counters get <c>_total</c>).
/// </summary>
public static class DashboardMetrics
{
    public const string MeterName = "Tropicast.Dashboard";

    private static readonly Meter Meter = new(MeterName);

    /// <summary><c>tropicast_up</c>: 1 while the API exports metrics; its absence means the API is down.</summary>
    public static readonly ObservableGauge<int> Up = Meter.CreateObservableGauge("tropicast.up", () => 1,
        description: "1 while the API runs and exports metrics.");

    /// <summary><c>tropicast_source_auth_decisions_total{result="allow|deny|error"}</c>: every Icecast source-auth call.</summary>
    public static readonly Counter<long> SourceAuthDecisions = Meter.CreateCounter<long>("tropicast.source_auth.decisions",
        description: "Icecast source-auth calls, by result: allow, deny (rules), error (failed closed).");

    /// <summary><c>tropicast_source_auth_duration_seconds</c>: time to answer Icecast.</summary>
    public static readonly Histogram<double> SourceAuthDuration = Meter.CreateHistogram<double>("tropicast.source_auth.duration", "s",
        "Time to answer an Icecast source-auth call.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });

    /// <summary><c>tropicast_provisioning_applies_total{result="ok|failed"}</c>: station limits pushed to a node.</summary>
    public static readonly Counter<long> ProvisioningApplies = Meter.CreateCounter<long>("tropicast.provisioning.applies",
        description: "Station-limit applies to a streaming node, by result: ok, failed.");

    /// <summary><c>tropicast_directory_submissions_total{result="added|adopted|failed"}</c>: RadioBrowser listing.</summary>
    public static readonly Counter<long> DirectorySubmissions = Meter.CreateCounter<long>("tropicast.directory.submissions",
        description: "RadioBrowser listing attempts, by result: added, adopted, failed.");

    /// <summary><c>tropicast_stats_samples_total{result="ok|unreachable"}</c>: streaming node samples.</summary>
    public static readonly Counter<long> StatsSamples = Meter.CreateCounter<long>("tropicast.stats.samples",
        description: "Streaming-node stats samples, by result: ok, unreachable.");

    public static KeyValuePair<string, object?> Result(string value) => new("result", value);
}
