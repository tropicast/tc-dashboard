namespace Tropicast.Dashboard.Api.Stations;

/// <summary>Where new stations stream (<c>Streaming</c> section). The MVP has one node.</summary>
internal sealed class StreamingOptions
{
    /// <summary>Icecast node for new stations, e.g. <c>tc-stream-1</c>.</summary>
    public string Node { get; set; } = "tc-stream-1";
    /// <summary>Public listener host; listener URLs are <c>{base}{mount}</c>.</summary>
    public Uri ListenerBaseUrl { get; set; } = new("https://listen.tropicastradio.com");
}
