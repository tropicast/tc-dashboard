namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>RadioBrowser listing (<c>Directory</c> section).</summary>
public sealed class DirectoryOptions
{
    /// <summary>Submit listings. Off by default: only production should add stations to the public directory.</summary>
    public bool Enabled { get; set; }
    /// <summary>How often to look for opted-in stations that went live.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>First retry after a failure; doubles up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(6);
    /// <summary>
    /// RadioBrowser API servers. Empty: discovered as RadioBrowser asks, from the addresses of
    /// <c>all.api.radio-browser.info</c> and their reverse DNS names.
    /// </summary>
    public List<Uri> Servers { get; set; } = [];
    /// <summary>Sent as User-Agent, as RadioBrowser asks of API clients.</summary>
    public string UserAgent { get; set; } = "Tropicast/1.0 (+https://tropicastradio.com)";
    /// <summary>Public listener host; the API sets it from <c>Streaming:ListenerBaseUrl</c>.</summary>
    public Uri ListenerBaseUrl { get; set; } = new("https://listen.tropicastradio.com");
    /// <summary>Web app origin, for public page links; the API sets it from <c>App:PublicBaseUrl</c>.</summary>
    public Uri PublicBaseUrl { get; set; } = new("https://app.tropicastradio.com");
}
