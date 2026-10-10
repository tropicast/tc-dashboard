namespace Tropicast.Dashboard.Api.Stations;

/// <summary>Where new stations stream (<c>Streaming</c> section). The MVP has one node.</summary>
internal sealed class StreamingOptions
{
    /// <summary>Icecast node for new stations, e.g. <c>tc-stream-1</c>.</summary>
    public string Node { get; set; } = "tc-stream-1";
    /// <summary>Public listener host; listener URLs are <c>{base}{mount}</c>.</summary>
    public Uri ListenerBaseUrl { get; set; } = new("https://listen.tropicastradio.com");
    /// <summary>Icecast <c>&lt;sources&gt;</c> on the node.</summary>
    public int MaxSources { get; set; } = 50;
    /// <summary>
    /// Sum of station listener caps the node accepts. Oversubscribed on purpose: stations rarely sit at their cap,
    /// and Icecast serves at most its <c>&lt;clients&gt;</c> (1,500) at once. Lower it as the node fills up.
    /// </summary>
    public int MaxListenerCaps { get; set; } = 15_000;
}
