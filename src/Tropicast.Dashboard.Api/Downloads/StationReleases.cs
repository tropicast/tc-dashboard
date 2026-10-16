using System.Text.Json;
using System.Text.RegularExpressions;
using Tropicast.Dashboard.Application.Media;

namespace Tropicast.Dashboard.Api.Downloads;

/// <summary>One file of the mirrored release, as tc-station's <c>scripts/mirror-release.sh</c> describes it.</summary>
/// <param name="Name">File name in the bucket, e.g. <c>Tropicast-Station-0.2.1-win-x64.msix</c>.</param>
/// <param name="Platform">See <see cref="StationReleases.Platforms"/>.</param>
/// <param name="ContentType">MIME type the file is stored with.</param>
/// <param name="Size">Bytes.</param>
/// <param name="Sha256">Lowercase hex.</param>
internal sealed record ReleaseFile(string Name, string Platform, string ContentType, long Size, string Sha256);

/// <summary><c>releases/tc-station/latest/manifest.json</c>: the one tc-station release kept in the bucket (tc-station#72).</summary>
internal sealed record ReleaseManifest(string Version, string Tag, IReadOnlyList<ReleaseFile> Files);

/// <summary>
/// The tc-station release mirrored to the private bucket (#127). Reads its manifest, cached a few minutes so a
/// download costs no bucket request beyond the presigned GET; a missing or unreadable manifest is retried sooner.
/// </summary>
internal sealed partial class StationReleases(IObjectStorage storage, TimeProvider time, ILogger<StationReleases> logger) : IDisposable
{
    internal const string Prefix = "releases/tc-station/latest/";
    internal const string ManifestKey = Prefix + "manifest.json";
    internal static readonly Uri GitHubReleases = new("https://github.com/tropicast/tc-station/releases/latest");

    /// <summary>Installers, in the order they are offered.</summary>
    internal static readonly string[] Installers = ["win-x64", "osx-arm64", "osx-x64", "linux-x64"];

    /// <summary>Every platform a download link may name: installers, then the files that go with them.</summary>
    internal static readonly string[] Platforms = [.. Installers, "certificate", "checksums", "script", "readme"];

    private const int MaxManifestBytes = 64 * 1024;
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (ReleaseManifest? Manifest, DateTimeOffset Until) _cached;

    /// <summary>The mirrored release; null when there is none, no bucket, or the bucket cannot be read now.</summary>
    internal async Task<ReleaseManifest?> LatestAsync(CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
        {
            return null;
        }
        if (time.GetUtcNow() < _cached.Until)
        {
            return _cached.Manifest;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (time.GetUtcNow() < _cached.Until)
            {
                return _cached.Manifest;
            }
            var manifest = await ReadAsync(cancellationToken);
            _cached = (manifest, time.GetUtcNow() + (manifest is null ? RetryAfter : Fresh));
            return manifest;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The presigned GET of a file, saved by the browser under its own name.</summary>
    internal Uri DownloadUrl(ReleaseFile file, TimeSpan lifetime) => storage.PresignDownload(Prefix + file.Name, lifetime, file.Name);

    private async Task<ReleaseManifest?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await storage.HeadAsync(ManifestKey, cancellationToken) is not { Size: > 0 and <= MaxManifestBytes } head)
            {
                LogNoManifest(logger);
                return null;
            }
            var bytes = await storage.ReadRangeAsync(ManifestKey, 0, (int)head.Size, cancellationToken);
            return Valid(JsonSerializer.Deserialize<ReleaseManifest>(bytes, Json));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Storage down or a manifest we do not understand: downloads fall back to GitHub until it reads again.
            LogUnreadable(logger, e.GetType().Name);
            return null;
        }
    }

    /// <summary>Only files whose name is a plain file name and whose platform is known; the first per platform.</summary>
    private static ReleaseManifest? Valid(ReleaseManifest? manifest)
    {
        if (manifest is not { Version.Length: > 0, Files: not null })
        {
            return null;
        }
        var files = manifest.Files
            .Where(f => f is not null && FileName().IsMatch(f.Name ?? "") && Platforms.Contains(f.Platform) && f.Size > 0)
            .DistinctBy(f => f.Platform)
            .ToList();
        return files.Count == 0 ? null : manifest with { Files = files };
    }

    public void Dispose() => _gate.Dispose();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex FileName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No tc-station release in the bucket: downloads point to GitHub")]
    private static partial void LogNoManifest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tc-station release manifest cannot be read ({ErrorType}): downloads point to GitHub")]
    private static partial void LogUnreadable(ILogger logger, string errorType);
}
