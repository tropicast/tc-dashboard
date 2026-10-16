using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;

namespace Tropicast.Dashboard.Api.Downloads;

/// <summary>A file of the desktop app's latest release.</summary>
/// <param name="Platform">win-x64, osx-arm64, osx-x64, linux-x64; or certificate, checksums, script, readme.</param>
/// <param name="Name">File name, e.g. Tropicast-Station-0.2.1-win-x64.msix.</param>
/// <param name="Size">Bytes.</param>
/// <param name="Sha256">Lowercase hex, also in the checksums file.</param>
/// <param name="Url">Download link on this site; it never expires.</param>
internal sealed record StationDownloadResponse(string Platform, string Name, long Size, string Sha256, Uri Url);

/// <summary>The desktop app's latest release.</summary>
/// <param name="Version">Version, e.g. 0.2.1; null when no release can be served from here now (use ReleasesPage).</param>
/// <param name="ReleasesPage">The release on GitHub: the fallback.</param>
/// <param name="Files">Installers first, then the certificate, checksums, Windows install script and install guide.</param>
internal sealed record StationDownloadsResponse(string? Version, Uri ReleasesPage, IReadOnlyList<StationDownloadResponse> Files);

/// <summary>
/// Downloads of Tropicast Station (#127) from tc-dashboard's own address. The files stay in the private bucket: a link
/// answers 302 to a short-lived presigned GET, so the bytes never pass through the API.
/// </summary>
internal static class DownloadEndpoints
{
    internal const string Path = "/download/station";

    /// <summary>A presigned link only has to last until the browser follows the redirect and the download starts.</summary>
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

    internal static void MapDownloads(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/downloads/station", ListAsync).WithTags("Downloads").AllowAnonymous()
            .RequireRateLimiting(AuthSetup.PublicRateLimit)
            .WithSummary("The desktop app's latest release: version and download links.");
        app.MapGet(Path + "/{platform}", DownloadAsync).AllowAnonymous().ExcludeFromDescription()
            .RequireRateLimiting(AuthSetup.PublicRateLimit);
    }

    /// <summary>Download links of the latest release, for the web app and emails; GitHub when none can be served.</summary>
    internal static async Task<StationDownloadsResponse> LinksAsync(StationReleases releases, string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        var manifest = await releases.LatestAsync(cancellationToken);
        if (manifest is null)
        {
            return new StationDownloadsResponse(null, StationReleases.GitHubReleases, []);
        }
        var files = manifest.Files.OrderBy(f => Array.IndexOf(StationReleases.Platforms, f.Platform))
            .Select(f => new StationDownloadResponse(f.Platform, f.Name, f.Size, f.Sha256, Link(publicBaseUrl, f.Platform)))
            .ToList();
        return new StationDownloadsResponse(manifest.Version, StationReleases.GitHubReleases, files);
    }

    internal static Uri Link(string publicBaseUrl, string platform) => new($"{publicBaseUrl.TrimEnd('/')}{Path}/{platform}");

    private static async Task<Ok<StationDownloadsResponse>> ListAsync(HttpContext context, StationReleases releases,
        IOptions<AppOptions> app, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Ok(await LinksAsync(releases, app.Value.PublicBaseUrl, cancellationToken));
    }

    private static async Task<Results<RedirectHttpResult, NotFound>> DownloadAsync(string platform, HttpContext context,
        StationReleases releases, CancellationToken cancellationToken)
    {
        if (!StationReleases.Platforms.Contains(platform))
        {
            return TypedResults.NotFound();
        }
        // The presigned URL is a secret for a few minutes: never cached, never stored by a shared cache.
        context.Response.Headers.CacheControl = "no-store";
        var file = (await releases.LatestAsync(cancellationToken))?.Files.FirstOrDefault(f => f.Platform == platform);
        // Nothing to serve from here (no mirror yet, storage down): the same release on GitHub.
        return TypedResults.Redirect(file is null ? StationReleases.GitHubReleases.AbsoluteUri : releases.DownloadUrl(file, LinkLifetime).AbsoluteUri);
    }
}
