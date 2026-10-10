using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Provisioning;

/// <summary>One station's limits on a node.</summary>
public sealed record StationLimits(string PublicId, Plan Plan);

/// <summary>A rendered <c>stations.json</c> and its version (hash of the content).</summary>
public sealed record RenderedStations(string Json, string Version);

/// <summary>
/// Renders the desired state of a streaming node as tc-streaming's <c>stations.json</c> (README, "Station limits"):
/// <c>{"default": {"max_listeners": N}, "stations": {"&lt;id&gt;": {plan, max_listeners, max_bitrate_kbps, formats}}}</c>.
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
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        var text = Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        return new RenderedStations(text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16]);
    }
}
