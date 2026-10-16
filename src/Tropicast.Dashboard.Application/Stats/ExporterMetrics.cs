using System.Globalization;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Application.Stats;

/// <summary>
/// Parses the tc-streaming exporter's Prometheus text (<c>exporter/icecast_exporter.py</c>) into a
/// <see cref="NodeSample"/>. Only the metrics and labels it needs; anything else is ignored.
/// </summary>
public static class ExporterMetrics
{
    public static NodeSample Parse(string node, string text, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mounts = new Dictionary<(string Station, AudioFormat Format, bool AutoDj), MountFields>();
        var icecastUp = false;
        long? egress = null, included = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || !TryParse(line, out var name, out var labels, out var value))
            {
                continue;
            }
            switch (name)
            {
                case "icecast_up":
                    icecastUp = value >= 1;
                    break;
                case "hetzner_server_outgoing_traffic_bytes":
                    egress = (long)value;
                    break;
                case "hetzner_server_included_traffic_bytes":
                    included = (long)value;
                    break;
                default:
                    // Live mounts are icecast_mount_* with station and format labels; AutoDJ mounts are repeated as
                    // tc_autodj_mount_*, keyed the same way: up, listeners, sent bytes (tc-streaming#66), and since
                    // tc-streaming#77 the start time and the declared bitrate. Older exporters send neither: the
                    // AutoDJ mount then has no start time and a bitrate of 0, and egress falls back to sent bytes. An
                    // icecast_mount_* series of an AutoDJ mount is skipped even when labeled, or it would count twice.
                    var autoDj = name.StartsWith(AutoDjPrefix, StringComparison.Ordinal);
                    if ((autoDj || (name.StartsWith(LivePrefix, StringComparison.Ordinal) && !IsAutoDjMount(labels)))
                        && labels.TryGetValue("station", out var station)
                        && labels.TryGetValue("format", out var format) && Format(format) is { } audio)
                    {
                        var key = (station, audio, autoDj);
                        var fields = mounts.TryGetValue(key, out var known) ? known : new MountFields();
                        mounts[key] = Apply(fields, name[(autoDj ? AutoDjPrefix : LivePrefix).Length..], value);
                    }
                    break;
            }
        }
        var samples = icecastUp
            ? mounts.Where(m => m.Value.Up).Select(m => new MountSample(m.Key.Station, m.Key.Format,
                (int)Math.Max(0, m.Value.Listeners), Math.Max(0, m.Value.Bitrate), (long)Math.Max(0, m.Value.SentBytes),
                m.Value.Start > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(m.Value.Start * 1000)) : null,
                m.Key.AutoDj)).ToList()
            : [];
        return new NodeSample(node, at, icecastUp, samples, egress, included);
    }

    private const string LivePrefix = "icecast_mount_";

    private static bool IsAutoDjMount(Dictionary<string, string> labels)
        => labels.TryGetValue("mount", out var mount) && mount.Contains("/autodj.", StringComparison.Ordinal);

    private const string AutoDjPrefix = "tc_autodj_mount_";

    private sealed record MountFields(bool Up = false, double Listeners = 0, double Bitrate = 0, double SentBytes = 0, double Start = 0);

    private static MountFields Apply(MountFields fields, string name, double value) => name switch
    {
        "up" => fields with { Up = value >= 1 },
        "listeners" => fields with { Listeners = value },
        "bitrate_kbps" => fields with { Bitrate = value },
        "sent_bytes_total" => fields with { SentBytes = value },
        "start_timestamp_seconds" => fields with { Start = value },
        _ => fields,
    };

    private static AudioFormat? Format(string value) => value switch
    {
        "mp3" => AudioFormat.Mp3,
        "opus" => AudioFormat.Opus,
        _ => null,
    };

    /// <summary><c>name{a="x",b="y"} 1.5</c> or <c>name 1.5</c>, with escaped quotes in label values.</summary>
    public static bool TryParse(string line, out string name, out Dictionary<string, string> labels, out double value)
    {
        labels = [];
        value = 0;
        var brace = line.IndexOf('{', StringComparison.Ordinal);
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            name = "";
            return false;
        }
        string rest;
        if (brace >= 0 && brace < space)
        {
            name = line[..brace];
            var i = brace + 1;
            while (i < line.Length && line[i] != '}')
            {
                var eq = line.IndexOf('=', i);
                if (eq < 0 || eq + 1 >= line.Length || line[eq + 1] != '"')
                {
                    return false;
                }
                var key = line[i..eq].Trim();
                var text = new System.Text.StringBuilder();
                var j = eq + 2;
                for (; j < line.Length && line[j] != '"'; j++)
                {
                    if (line[j] == '\\' && j + 1 < line.Length)
                    {
                        j++;
                        text.Append(line[j] == 'n' ? '\n' : line[j]);
                    }
                    else
                    {
                        text.Append(line[j]);
                    }
                }
                labels[key] = text.ToString();
                i = j + 1;
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
            rest = i < line.Length ? line[(i + 1)..] : "";
        }
        else
        {
            name = line[..space];
            rest = line[space..];
        }
        var token = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is not null && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
