using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Media;

/// <summary>
/// A deleted station keeps its history, not its uploaded audio: every object under its prefix goes, then its track
/// rows. Idempotent (outbox delivery is at least once). Deleting an account needs its stations deleted first, so
/// this also covers <c>DELETE /auth/account</c>. Uploads still pending stay until <see cref="PendingUploadCleanup"/>
/// takes them: their URL may still PUT a file after the prefix is cleared, and that cleanup deletes it.
/// </summary>
internal sealed class StationTracksCleanup(IServiceScopeFactory scopes, TimeProvider time) : IOutboxConsumer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<string> EventTypes { get; } = [nameof(StationDeleted)];

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var deleted = JsonSerializer.Deserialize<StationDeleted>(message.Payload, Json)
            ?? throw new JsonException("Empty StationDeleted payload.");
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        if (storage.IsConfigured)
        {
            await storage.DeletePrefixAsync(Track.StationPrefix(deleted.StationId), cancellationToken);
        }
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow();
        await db.Tracks.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => t.StationId == deleted.StationId && (t.Status == TrackStatus.Ready || t.UploadExpiresAt < now))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Uploads started but never confirmed: their object (if the browser sent one) and row go once expired.</summary>
internal sealed partial class PendingUploadCleanup(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<PendingUploadCleanup> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DeleteExpiredAsync(scopes, time.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    internal static async Task<int> DeleteExpiredAsync(IServiceScopeFactory scopes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var expired = await db.Tracks.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => t.Status == TrackStatus.Pending && t.UploadExpiresAt < now)
            .Select(t => new { t.Id, t.ObjectKey }).Take(500).ToListAsync(cancellationToken);
        foreach (var track in expired)
        {
            // The object first: a row deleted without its object would leave audio nobody can find.
            if (storage.IsConfigured)
            {
                await storage.DeleteAsync(track.ObjectKey, cancellationToken);
            }
            await db.Tracks.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.Id == track.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        return expired.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pending upload cleanup failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
