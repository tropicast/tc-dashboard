using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Application.Tests;

public sealed class StatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 3, 20, TimeSpan.Zero);

    /// <summary>The shape tc-streaming's exporter/icecast_exporter.py writes.</summary>
    private const string Exporter = """
        # HELP icecast_up 1 if Icecast answered the stats request.
        # TYPE icecast_up gauge
        icecast_up 1
        icecast_listeners 7
        # TYPE icecast_mount_up gauge
        icecast_mount_up{mount="/stations/k3m9x2p7qa/live.mp3",station="k3m9x2p7qa",format="mp3"} 1
        icecast_mount_up{mount="/stations/k3m9x2p7qa/live.opus",station="k3m9x2p7qa",format="opus"} 1
        icecast_mount_up{mount="/legacy.mp3"} 1
        icecast_mount_listeners{mount="/stations/k3m9x2p7qa/live.mp3",station="k3m9x2p7qa",format="mp3"} 5.0
        icecast_mount_listeners{mount="/stations/k3m9x2p7qa/live.opus",station="k3m9x2p7qa",format="opus"} 2.0
        icecast_mount_listeners{mount="/legacy.mp3"} 9.0
        icecast_mount_sent_bytes_total{mount="/stations/k3m9x2p7qa/live.mp3",station="k3m9x2p7qa",format="mp3"} 1.5e+06
        icecast_mount_start_timestamp_seconds{mount="/stations/k3m9x2p7qa/live.mp3",station="k3m9x2p7qa",format="mp3"} 1791633600.0
        icecast_mount_bitrate_kbps{mount="/stations/k3m9x2p7qa/live.mp3",station="k3m9x2p7qa",format="mp3"} 128
        icecast_mount_bitrate_kbps{mount="/stations/k3m9x2p7qa/live.opus",station="k3m9x2p7qa",format="opus"} 0
        # TYPE hetzner_server_outgoing_traffic_bytes gauge
        hetzner_server_outgoing_traffic_bytes{server="tc-stream-1"} 3.2e+12
        hetzner_server_included_traffic_bytes{server="tc-stream-1"} 2.2e+13
        """;

    [Fact]
    public void The_exporter_text_gives_station_mounts_and_node_traffic()
    {
        var sample = ExporterMetrics.Parse("tc-stream-1", Exporter, Now);

        Assert.True(sample.IcecastUp);
        Assert.Equal(2, sample.Mounts.Count);
        var mp3 = sample.Mounts.Single(m => m.Format == AudioFormat.Mp3);
        Assert.Equal(("k3m9x2p7qa", 5, 128.0, 1_500_000L), (mp3.PublicId, mp3.Listeners, mp3.BitrateKbps, mp3.SentBytes));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791633600), mp3.StartedAt);
        var opus = sample.Mounts.Single(m => m.Format == AudioFormat.Opus);
        Assert.Equal((2, 0.0, (DateTimeOffset?)null), (opus.Listeners, opus.BitrateKbps, opus.StartedAt));
        Assert.Equal(3_200_000_000_000L, sample.EgressBytes);
        Assert.Equal(22_000_000_000_000L, sample.IncludedBytes);
    }

    [Fact]
    public void Icecast_down_means_mounts_unknown()
    {
        var sample = ExporterMetrics.Parse("tc-stream-1", "icecast_up 0\nhetzner_up 0\n", Now);
        Assert.False(sample.IcecastUp);
        Assert.Empty(sample.Mounts);
        Assert.Null(sample.EgressBytes);
    }

    [Theory]
    [InlineData("""m{a="x\"y",b="z"} 2""", "m", "x\"y", 2.0)]
    [InlineData("""m{a="1,2"} 3.5e2""", "m", "1,2", 350.0)]
    [InlineData("m 4", "m", null, 4.0)]
    public void Prometheus_lines_parse_with_escaped_labels(string line, string name, string? label, double value)
    {
        Assert.True(ExporterMetrics.TryParse(line, out var parsed, out var labels, out var number));
        Assert.Equal(name, parsed);
        Assert.Equal(label, labels.GetValueOrDefault("a"));
        Assert.Equal(value, number);
    }

    private static NodeSample Sample(DateTimeOffset at, params MountSample[] mounts) => new("tc-stream-1", at, true, mounts, null, null);

    private static MountSample Mp3(int listeners, double kbps = 128, long sent = 0) =>
        new("k3m9x2p7qa", AudioFormat.Mp3, listeners, kbps, sent, Now.AddHours(-1));

    [Fact]
    public void Listener_time_and_egress_follow_bitrate_and_overhead()
    {
        var first = Sample(Now, Mp3(100));
        var second = Sample(Now.AddSeconds(15), Mp3(100), new MountSample("k3m9x2p7qa", AudioFormat.Opus, 20, 64, 0, null));

        var increment = Assert.Single(ListenerAccounting.Increments(first, second, TimeSpan.FromSeconds(90)));

        Assert.Equal(120, increment.PeakListeners);
        Assert.Equal(120 * 15, increment.ListenerSeconds);
        // (128 kbps × 100 + 64 kbps × 20) listeners × 15 s, × 1.10 overhead.
        Assert.Equal((long)Math.Round(((128_000 / 8 * 100) + (64_000 / 8 * 20)) * 15 * 1.10), increment.EgressBytes);
    }

    [Fact]
    public void Without_a_bitrate_the_sent_bytes_delta_is_used()
    {
        var first = Sample(Now, Mp3(10, kbps: 0, sent: 1_000_000));
        var second = Sample(Now.AddSeconds(15), Mp3(10, kbps: 0, sent: 3_000_000));

        Assert.Equal((long)Math.Round(2_000_000 * 1.10), ListenerAccounting.Increments(first, second, TimeSpan.FromSeconds(90))[0].EgressBytes);
    }

    [Fact]
    public void A_first_sample_or_a_long_gap_records_only_the_peak()
    {
        var after = Sample(Now.AddMinutes(10), Mp3(50));

        foreach (var previous in new[] { null, Sample(Now, Mp3(50)) })
        {
            var increment = Assert.Single(ListenerAccounting.Increments(previous, after, TimeSpan.FromSeconds(90)));
            Assert.Equal((50, 0.0, 0L), (increment.PeakListeners, increment.ListenerSeconds, increment.EgressBytes));
        }
    }

    /// <summary>
    /// tc-streaming docs/load-test.md, 24 h soak: Opus received 20.6 MB of payload per listener-hour (about 45.8 kbps
    /// VBR). One hour of 150 Opus listeners sampled every 15 s gives 150 listener-hours and, with the 1.10 factor,
    /// egress within 10% of 20.6 MB × 1.10 per listener-hour.
    /// </summary>
    [Fact]
    public void A_test_hour_matches_the_load_test_figures()
    {
        double listenerSeconds = 0;
        long egress = 0;
        NodeSample? previous = null;
        for (var i = 0; i <= 240; i++)
        {
            var sample = Sample(Now.AddSeconds(15 * i), new MountSample("k3m9x2p7qa", AudioFormat.Opus, 150, 45.8, 0, Now));
            foreach (var increment in ListenerAccounting.Increments(previous, sample, TimeSpan.FromSeconds(90)))
            {
                listenerSeconds += increment.ListenerSeconds;
                egress += increment.EgressBytes;
            }
            previous = sample;
        }

        var listenerHours = listenerSeconds / 3600;
        Assert.InRange(listenerHours, 150 * 0.9, 150 * 1.1);
        var expected = 150 * 20.6e6 * 1.10;
        Assert.InRange(egress, expected * 0.9, expected * 1.1);
    }

    [Fact]
    public void Periods_are_utc_five_minutes_and_days()
    {
        var at = new DateTimeOffset(2026, 10, 10, 23, 59, 59, TimeSpan.FromHours(3));
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 20, 55, 0, TimeSpan.Zero), ListenerAccounting.FiveMinutePeriod(at));
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), ListenerAccounting.DayPeriod(at));
    }
}
