using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Directories;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>
/// Adds opted-in stations to RadioBrowser once they are live, once each. RadioBrowser has no idempotent add, so:
/// before adding, look the stream URL up there (an earlier add whose result was lost is adopted, not repeated);
/// record the attempt durably before calling (and wait a while after an unconfirmed one); record the result right
/// after. Never throws for one station: failures are recorded on <see cref="DirectoryListing"/> and retried with
/// backoff; nothing else waits for it.
/// </summary>
internal sealed partial class DirectoryPublisher(AppDbContext db, IRadioDirectory directory, IOptions<DirectoryOptions> options,
    TimeProvider time, ILogger<DirectoryPublisher> logger)
{
    /// <summary>Time for a new entry to replicate to every RadioBrowser server before its URL is looked up.</summary>
    internal static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(10);

    public async Task PublishAsync(CancellationToken cancellationToken)
    {
        // Opted in, not deleted, live now, not added yet.
        var live = db.LiveSessions.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(l => l.EndedAt == null);
        var candidates = await db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(s => s.ListInDirectory)
            .Where(s => live.Any(l => l.StationId == s.Id))
            .Where(s => !db.DirectoryListings.Any(l => l.StationId == s.Id && l.RadioBrowserUuid != null))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }
        var entries = await DirectoryEntries.ForAsync(db, options.Value, candidates, cancellationToken);
        foreach (var (stationId, entry) in entries.Where(e => e.Value.Listed))
        {
            try
            {
                await PublishAsync(stationId, entry, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another instance changed the same listing (row version or new row): it handles this station.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task PublishAsync(Guid stationId, DirectoryEntry entry, CancellationToken cancellationToken)
    {
        var listing = await db.DirectoryListings.SingleOrDefaultAsync(l => l.StationId == stationId, cancellationToken);
        if (listing is null)
        {
            listing = DirectoryListing.For(stationId);
            db.DirectoryListings.Add(listing);
        }
        var now = time.GetUtcNow();
        if (entry.Problem is { } problem)
        {
            listing.Blocked(problem);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        if (!listing.Due(now) || listing.AwaitingConfirmation(now, ConfirmationWindow))
        {
            return;
        }
        try
        {
            if (await directory.FindAsync(entry.StreamUrl, cancellationToken) is { } existing)
            {
                listing.Added(existing, time.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                LogAdopted(logger, stationId, existing);
                Telemetry.DashboardMetrics.DirectorySubmissions.Add(1, Telemetry.DashboardMetrics.Result("adopted"));
                return;
            }
            // Durable before the call: if the result is lost, the next pass looks the URL up instead of adding again.
            listing.Requested(now);
            await db.SaveChangesAsync(cancellationToken);
            var uuid = await directory.AddAsync(entry, cancellationToken);
            listing.Added(uuid, time.GetUtcNow());
            LogAdded(logger, stationId, uuid);
            Telemetry.DashboardMetrics.DirectorySubmissions.Add(1, Telemetry.DashboardMetrics.Result("added"));
        }
        catch (DirectoryException ex)
        {
            listing.Failed(ex.Message, time.GetUtcNow(), options.Value.RetryDelay, options.Value.MaxRetryDelay);
            LogFailed(logger, stationId, listing.FailedAttempts);
            Telemetry.DashboardMetrics.DirectorySubmissions.Add(1, Telemetry.DashboardMetrics.Result("failed"));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Station {StationId} added to RadioBrowser as {Uuid}")]
    private static partial void LogAdded(ILogger logger, Guid stationId, string uuid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Station {StationId} already on RadioBrowser as {Uuid}; recorded")]
    private static partial void LogAdopted(ILogger logger, Guid stationId, string uuid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Adding station {StationId} to RadioBrowser failed, attempt {Attempts}; will retry")]
    private static partial void LogFailed(ILogger logger, Guid stationId, int attempts);
}

/// <summary>Runs <see cref="DirectoryPublisher"/> every <see cref="DirectoryOptions.Interval"/> when enabled.</summary>
internal sealed partial class DirectoryWorker(IServiceScopeFactory scopes, IOptions<DirectoryOptions> options, TimeProvider time,
    ILogger<DirectoryWorker> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(options.Value.Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DirectoryPublisher>().PublishAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Directory pass failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
