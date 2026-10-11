using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Provisioning;

/// <summary>One station's limits on a node, and its directory metadata when it is (or was) listed.</summary>
public sealed record StationLimits(string PublicId, Plan Plan, Directories.DirectoryEntry? Directory = null);

/// <summary>A rendered <c>stations.json</c> and its version (hash of the content).</summary>
public sealed record RenderedStations(string Json, string Version);

/// <summary>
/// Renders the desired state of a streaming node as tc-streaming's <c>stations.json</c> (README, "Station limits"):
/// <c>{"default": {"max_listeners": N}, "stations": {"&lt;id&gt;": {plan, max_listeners, max_bitrate_kbps, formats, directory?}}}</c>.
/// <c>directory</c> makes the node send the station's metadata as <c>icy-*</c> headers, which directories such as
/// RadioBrowser read to update a listing (tc-streaming README, "Station limits").
/// Deterministic: the same stations and plans always give the same bytes and version.
/// </summary>
public static class StationLimitsDocument
{
    public static RenderedStations Render(IEnumerable<StationLimits> stations, int defaultMaxListeners)
    {
        ArgumentNullException.ThrowIfNull(stations);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("default");
            json.WriteNumber("max_listeners", defaultMaxListeners);
            json.WriteEndObject();
            json.WriteStartObject("stations");
            foreach (var station in stations.OrderBy(s => s.PublicId, StringComparer.Ordinal))
            {
                json.WriteStartObject(station.PublicId);
                json.WriteString("plan", station.Plan.Id);
                json.WriteNumber("max_listeners", station.Plan.MaxListeners);
                json.WriteNumber("max_bitrate_kbps", station.Plan.MaxBitrateKbps);
                json.WriteStartArray("formats");
                if (station.Plan.Allows(AudioFormat.Mp3))
                {
                    json.WriteStringValue("mp3");
                }
                if (station.Plan.Allows(AudioFormat.Opus))
                {
                    json.WriteStringValue("opus");
                }
                json.WriteEndArray();
                if (station.Directory is { } directory)
                {
                    WriteDirectory(json, directory);
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        var text = Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        return new RenderedStations(text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16]);
    }

    private static void WriteDirectory(Utf8JsonWriter json, Directories.DirectoryEntry directory)
    {
        json.WriteStartObject("directory");
        json.WriteBoolean("listed", directory.Listed);
        if (directory.Listed)
        {
            Optional(json, "name", Clean(directory.Name, 400));
            Optional(json, "description", Clean(directory.Description, 512));
            Optional(json, "genre", Clean(directory.Genre, 256));
            Optional(json, "homepage", Url(directory.Homepage));
            Optional(json, "logo", Url(directory.Logo));
            Optional(json, "country_code", directory.CountryCode);
            Optional(json, "language_codes", directory.LanguageCodes);
            Optional(json, "main_stream_url", Url(directory.StreamUrl));
        }
        json.WriteEndObject();
    }

    private static void Optional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(name, value);
        }
    }

    /// <summary>
    /// An http(s) URL the node accepts (at most 512 characters, no spaces), else left out: one value the node
    /// refuses would fail the whole apply.
    /// </summary>
    private static string? Url(Uri? url)
        => url is { Scheme: "http" or "https", UserInfo.Length: 0 } && url.AbsoluteUri.Length <= 512 && !url.AbsoluteUri.Any(char.IsWhiteSpace)
            ? url.AbsoluteUri
            : null;

    /// <summary>The node refuses control characters in header values: turn line breaks and tabs into spaces.</summary>
    private static string? Clean(string? text, int max)
    {
        if (text is null)
        {
            return null;
        }
        // Control characters (C0, DEL, C1) and line/paragraph separators: the node refuses them all.
        var value = new string([.. text.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c)]).Trim();
        return value.Length <= max ? value : value[..max];
    }
}
