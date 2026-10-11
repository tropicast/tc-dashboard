using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Infrastructure.Directories;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>Where a station's RadioBrowser listing stands.</summary>
internal enum DirectoryState
{
    /// <summary>The station is not opted in.</summary>
    Off,
    /// <summary>The plan does not include directory listing.</summary>
    NotIncluded,
    /// <summary>Something the owner must fix first (see <see cref="DirectoryStatusResponse.Message"/>).</summary>
    NeedsAction,
    /// <summary>Opted in: added the next time the station is live.</summary>
    WaitingForLive,
    /// <summary>The last attempt failed; retried at <see cref="DirectoryStatusResponse.NextAttemptAt"/>.</summary>
    Retrying,
    /// <summary>Listed; changes reach RadioBrowser from the stream, at its next check.</summary>
    Listed,
    /// <summary>Opted out after listing: at its next check of the stream RadioBrowser skips it and marks the entry broken.</summary>
    Delisting,
}

/// <summary>A station's RadioBrowser listing.</summary>
/// <param name="State">Where the listing stands.</param>
/// <param name="Message">What to do, or the last failure.</param>
/// <param name="RadioBrowserUuid">RadioBrowser station ID once listed.</param>
/// <param name="ListingUrl">The listing on radio-browser.info.</param>
/// <param name="ListedAt">When it was added.</param>
/// <param name="NextAttemptAt">Next retry after a failure.</param>
/// <param name="Submitting">Listing is switched on for this deployment (off in development).</param>
internal sealed record DirectoryStatusResponse(DirectoryState State, string? Message, string? RadioBrowserUuid, Uri? ListingUrl,
    DateTimeOffset? ListedAt, DateTimeOffset? NextAttemptAt, bool Submitting);

/// <summary>RadioBrowser listing status (#13). The setting itself is the station's <c>listInDirectory</c>.</summary>
internal static class DirectoryEndpoints
{
    internal static void MapDirectory(this IEndpointRouteBuilder app)
        => app.MapGet("/api/v1/stations/{id:guid}/directory", GetAsync).WithTags("Stations")
            .RequireAuthorization(TenantPolicies.Member)
            .WithSummary("The station's RadioBrowser listing: off, waiting for live, listed, retrying, or what to fix.");

    private static async Task<Results<Ok<DirectoryStatusResponse>, NotFound>> GetAsync(Guid id, AppDbContext db,
        IOptions<DirectoryOptions> options, CancellationToken cancellationToken)
    {
        var station = await db.Stations.Where(s => s.Id == id)
            .Join(db.Tenants, s => s.TenantId, t => t.Id, (s, t) => new { s.Id, s.ListInDirectory, t.PlanId })
            .Join(db.Plans, x => x.PlanId, p => p.Id, (x, p) => new { x.Id, x.ListInDirectory, p.DirectoryListing })
            .SingleOrDefaultAsync(cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        var listing = await db.DirectoryListings.AsNoTracking().SingleOrDefaultAsync(l => l.StationId == id, cancellationToken);
        var entries = await DirectoryEntries.ForAsync(db, options.Value, [id], cancellationToken);
        var entry = entries.GetValueOrDefault(id);
        var submitted = listing?.Submitted == true;
        var (state, message) = Describe(station.DirectoryListing, station.ListInDirectory, submitted, entry?.Problem, listing);
        var uuid = listing?.RadioBrowserUuid;
        return TypedResults.Ok(new DirectoryStatusResponse(state, message, uuid,
            uuid is null ? null : new Uri($"https://www.radio-browser.info/history/{Uri.EscapeDataString(uuid)}"),
            listing?.ListedAt, listing?.NextAttemptAt, options.Value.Enabled));
    }

    private static (DirectoryState, string?) Describe(bool planAllows, bool optedIn, bool submitted, string? problem, DirectoryListing? listing)
    {
        if (!planAllows)
        {
            return submitted
                ? (DirectoryState.Delisting, "The plan no longer includes directory listing.")
                : (DirectoryState.NotIncluded, "Directory listing is included in the Starter plan and above.");
        }
        if (!optedIn)
        {
            return (submitted ? DirectoryState.Delisting : DirectoryState.Off, null);
        }
        if (submitted)
        {
            return (DirectoryState.Listed, null);
        }
        if (problem is not null)
        {
            return (DirectoryState.NeedsAction, problem);
        }
        return listing is { FailedAttempts: > 0 }
            ? (DirectoryState.Retrying, listing.LastError)
            : (DirectoryState.WaitingForLive, "Added to RadioBrowser the next time the station is live.");
    }
}
