namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>
/// A station's listing in RadioBrowser. RadioBrowser can only add stations (each add makes a new entry), so the
/// station is added once and its ID kept: later changes reach the listing through the stream's headers
/// (<c>stations.json</c> directory metadata on the node), never through another add.
/// </summary>
public sealed class DirectoryListing
{
    private DirectoryListing()
    {
    }

    public Guid StationId { get; private set; }
    /// <summary>RadioBrowser station UUID once added.</summary>
    public string? RadioBrowserUuid { get; private set; }
    public DateTimeOffset? ListedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    /// <summary>Last failure or blocking problem, safe to show to the station owner.</summary>
    public string? LastError { get; private set; }

    public bool Submitted => RadioBrowserUuid is not null;

    internal static DirectoryListing For(Guid stationId) => new() { StationId = stationId };

    internal bool Due(DateTimeOffset now) => NextAttemptAt is null || now >= NextAttemptAt;

    internal void Added(string uuid, DateTimeOffset now)
    {
        RadioBrowserUuid = uuid;
        ListedAt = now;
        LastAttemptAt = now;
        FailedAttempts = 0;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>Exponential backoff from <paramref name="baseDelay"/>, capped at <paramref name="maxDelay"/>.</summary>
    internal void Failed(string error, DateTimeOffset now, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        LastAttemptAt = now;
        FailedAttempts++;
        var delay = baseDelay * Math.Pow(2, Math.Min(FailedAttempts - 1, 20));
        NextAttemptAt = now + (delay < maxDelay ? delay : maxDelay);
        LastError = error.Length <= 300 ? error : error[..300];
    }

    /// <summary>Not submitted, for a reason the owner has to fix (checked again on every pass, no backoff).</summary>
    internal void Blocked(string problem) => LastError = problem;
}
