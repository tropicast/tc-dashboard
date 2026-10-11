using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Directories;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Directories;

/// <summary>
/// Adds opted-in stations to RadioBrowser once they are live, once each. Never throws: failures are recorded on
/// <see cref="DirectoryListing"/> and retried with backoff; nothing else waits for it.
/// </summary>
internal sealed partial class DirectoryPublisher(AppDbContext db, IRadioDirectory directory, IOptions<DirectoryOptions> options,
    TimeProvider time, ILogger<DirectoryPublisher> logger)
{
    private const long LockKey = 0x54_43_44_49_52; // "TCDIR"

    public async Task PublishAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockKey}) AS \"Value\"").SingleAsync(cancellationToken);
        if (!locked)
        {
            return;
        }
        var now = time.GetUtcNow();
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
        var listings = await db.DirectoryListings.Where(l => candidates.Contains(l.StationId)).ToDictionaryAsync(l => l.StationId, cancellationToken);
        foreach (var (stationId, entry) in entries.Where(e => e.Value.Listed))
        {
            if (!listings.TryGetValue(stationId, out var listing))
            {
                listing = DirectoryListing.For(stationId);
                db.DirectoryListings.Add(listing);
            }
            if (entry.Problem is { } problem)
            {
                listing.Blocked(problem);
                continue;
            }
            if (!listing.Due(now))
            {
                continue;
            }
            try
            {
                var uuid = await directory.AddAsync(entry, cancellationToken);
                listing.Added(uuid, time.GetUtcNow());
                LogAdded(logger, stationId, uuid);
            }
            catch (DirectoryException ex)
            {
                listing.Failed(ex.Message, time.GetUtcNow(), options.Value.RetryDelay, options.Value.MaxRetryDelay);
                LogFailed(logger, stationId, listing.FailedAttempts);
            }
            // Keep each result even if a later station's call is cancelled.
            await db.SaveChangesAsync(cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Station {StationId} added to RadioBrowser as {Uuid}")]
    private static partial void LogAdded(ILogger logger, Guid stationId, string uuid);

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
