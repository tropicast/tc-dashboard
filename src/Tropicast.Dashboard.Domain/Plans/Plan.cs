namespace Tropicast.Dashboard.Domain.Plans;

/// <summary>
/// A plan in the catalogue (docs/competitor-pricing-analysis.md). Listener caps are enforced by Icecast,
/// bitrate and formats by source auth, station count by the API.
/// </summary>
public sealed class Plan
{
    private Plan()
    {
    }

    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public int MaxStations { get; private set; }
    public int MaxListeners { get; private set; }
    public int MaxBitrateKbps { get; private set; }
    public AudioFormats Formats { get; private set; }
    public bool DirectoryListing { get; private set; }
    public bool Embed { get; private set; }
    public bool Analytics { get; private set; }

    public bool Allows(AudioFormat format) => Formats.HasFlag(format == AudioFormat.Mp3 ? AudioFormats.Mp3 : AudioFormats.Opus);

    public static Plan Free { get; } = new()
    {
        Id = "free", Name = "Free", SortOrder = 0, MaxStations = 1, MaxListeners = 100, MaxBitrateKbps = 64,
        Formats = AudioFormats.Mp3 | AudioFormats.Opus, DirectoryListing = false, Embed = false, Analytics = true,
    };

    public static Plan Starter { get; } = new()
    {
        Id = "starter", Name = "Starter", SortOrder = 1, MaxStations = 1, MaxListeners = 500, MaxBitrateKbps = 128,
        Formats = AudioFormats.Mp3 | AudioFormats.Opus, DirectoryListing = true, Embed = false, Analytics = true,
    };

    public static Plan Growth { get; } = new()
    {
        Id = "growth", Name = "Growth", SortOrder = 2, MaxStations = 3, MaxListeners = 5_000, MaxBitrateKbps = 192,
        Formats = AudioFormats.Mp3 | AudioFormats.Opus, DirectoryListing = true, Embed = true, Analytics = true,
    };

    public static Plan Pro { get; } = new()
    {
        Id = "pro", Name = "Pro", SortOrder = 3, MaxStations = 5, MaxListeners = 25_000, MaxBitrateKbps = 320,
        Formats = AudioFormats.Mp3 | AudioFormats.Opus, DirectoryListing = true, Embed = true, Analytics = true,
    };

    /// <summary>Every plan, cheapest first. Seeded into the database.</summary>
    public static IReadOnlyList<Plan> Catalogue { get; } = [Free, Starter, Growth, Pro];
}
