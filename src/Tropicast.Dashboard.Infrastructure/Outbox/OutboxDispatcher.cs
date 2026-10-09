using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    /// <summary>Off in tests that check the stored messages themselves.</summary>
    public bool Enabled { get; set; } = true;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 50;
}

/// <summary>
/// Delivers outbox messages to every <see cref="IOutboxConsumer"/> that handles their type. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several API instances can run it. A message is done when all its consumers succeed;
/// types no consumer handles are left pending (not claimed), so they wait without being polled.
/// </summary>
internal sealed partial class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(options.Value.PollInterval, time);
        do
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch: there may be more waiting.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Delivers one batch; returns how many messages it took.</summary>
    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var consumers = scope.ServiceProvider.GetServices<IOutboxConsumer>().ToList();
        var types = consumers.SelectMany(c => c.EventTypes).Distinct().ToArray();
        if (types.Length == 0)
        {
            // Nothing consumes events yet: keep them for when a consumer is registered.
            return 0;
        }
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT *, xmin FROM outbox_messages
                WHERE processed_at IS NULL AND type = ANY({types}) AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                ORDER BY occurred_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        foreach (var message in batch)
        {
            var envelope = new OutboxEnvelope(message.Id, message.Type, message.Payload, message.OccurredAt);
            try
            {
                foreach (var consumer in consumers.Where(c => c.EventTypes.Contains(message.Type)))
                {
                    await consumer.HandleAsync(envelope, cancellationToken);
                }
                message.Processed(time.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Failed(ex.GetType().Name, time.GetUtcNow());
                LogMessageFailed(logger, message.Type, message.Id, message.Attempts);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {Type} {Id} failed (attempt {Attempts}); retrying later")]
    private static partial void LogMessageFailed(ILogger logger, string type, Guid id, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed ({ErrorType})")]
    private static partial void LogBatchFailed(ILogger logger, string errorType);
}
