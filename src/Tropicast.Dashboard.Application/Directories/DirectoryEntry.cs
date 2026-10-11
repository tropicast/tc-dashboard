using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Application.Directories;

/// <summary>
/// What a station shows in public radio directories (RadioBrowser). Only public data: the public MP3 listener URL
/// and the station's public metadata; never ingest URLs, credentials or internal hostnames.
/// </summary>
/// <param name="Listed">Listed now. False for a station that was listed and opted out (or lost the plan feature).</param>
/// <param name="Name">Station name.</param>
/// <param name="Description">Description, at most 512 characters.</param>
/// <param name="Genre">Comma-separated lowercase tags.</param>
/// <param name="Homepage">The station's website, else its public page.</param>
/// <param name="Logo">Logo URL (http/https).</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2; RadioBrowser requires it.</param>
/// <param name="LanguageCodes">ISO 639 codes (the primary subtag of the station's language).</param>
/// <param name="StreamUrl">Public MP3 listener URL: plays in every directory app and car head unit.</param>
public sealed record DirectoryEntry(bool Listed, string Name, string? Description, string? Genre, Uri? Homepage, Uri? Logo,
    string? CountryCode, string? LanguageCodes, Uri StreamUrl)
{
    /// <summary>Why the station cannot be submitted yet, or null when it can.</summary>
    public string? Problem => CountryCode is null
        ? "Set the station's country: RadioBrowser requires it."
        : null;

    /// <summary>
    /// The entry of a station: listed when its owner opted in and the plan includes directory listing; delisted
    /// (Listed false) when it was submitted before but no longer qualifies; null when it never was and is not now.
    /// </summary>
    public static DirectoryEntry? For(Station station, Plan plan, bool submitted, Uri streamUrl, Uri publicPage)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(plan);
        var listed = station.ListInDirectory && plan.DirectoryListing && !station.IsDeleted;
        if (!listed && !submitted)
        {
            return null;
        }
        var language = station.Language?.Split('-')[0].ToLowerInvariant();
        return new DirectoryEntry(
            listed,
            station.Name,
            Trim(station.Description, 512),
            Tags(station.Genre),
            Public(station.Website) ?? publicPage,
            Public(station.LogoUrl),
            station.Country is { Length: 2 } country && country.All(char.IsAsciiLetter) ? country.ToUpperInvariant() : null,
            language is not null && Iso6391.Contains(language) ? language : null,
            streamUrl);
    }

    /// <summary>ISO 639-1 codes: the node and the directories accept only these (tc-streaming docker-entrypoint.py).</summary>
    private static readonly HashSet<string> Iso6391 =
    [
        .. """
        aa ab ae af ak am an ar as av ay az ba be bg bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ee el en eo es
        et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik io is it iu ja jv ka kg ki kj
        kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my na nb nd ne ng nl nn no nr nv
        ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti
        tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo wa wo xh yi yo za zh zu
        """.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries),
    ];

    /// <summary>An http(s) address safe to publish: never one carrying a user name or password.</summary>
    private static Uri? Public(Uri? url) => url is { IsAbsoluteUri: true, Scheme: "https" or "http", UserInfo.Length: 0 } ? url : null;

    private static string? Trim(string text, int max)
    {
        var value = text.Trim();
        return value.Length == 0 ? null : value.Length <= max ? value : value[..max];
    }

    /// <summary>"Salegy / Hira Gasy" becomes "salegy,hira gasy".</summary>
    private static string? Tags(string genre)
    {
        var tags = genre.Split([',', '/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct();
        var joined = string.Join(',', tags);
        return joined.Length == 0 ? null : joined.Length <= 256 ? joined : joined[..256];
    }
}

/// <summary>A public radio directory (RadioBrowser) that can add a station.</summary>
public interface IRadioDirectory
{
    /// <summary>Adds the station and returns its directory ID. Throws <see cref="DirectoryException"/> when refused or unreachable.</summary>
    Task<string> AddAsync(DirectoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>The ID of an existing entry with this stream URL, or null. Throws <see cref="DirectoryException"/> when unreachable.</summary>
    Task<string?> FindAsync(Uri streamUrl, CancellationToken cancellationToken = default);
}

/// <summary>A failed directory call; the message is safe to show to the station owner.</summary>
public sealed class DirectoryException : Exception
{
    public DirectoryException() : this("The directory call failed.") { }

    public DirectoryException(string message) : base(message) { }

    public DirectoryException(string message, Exception innerException) : base(message, innerException) { }
}
