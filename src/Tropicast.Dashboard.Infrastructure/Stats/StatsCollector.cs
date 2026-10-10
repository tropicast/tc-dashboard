using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Stats;

/// <summary>
/// Samples the streaming node every <see cref="StatsOptions.Interval"/>: live status (in memory), live sessions and
/// rollups (database). Hourly, deletes 5-minute rollups past their retention.
/// </summary>
internal sealed partial class StatsCollector(IStreamingStatsClient client, LiveStatusStore store, IServiceScopeFactory scopes,
    IOptions<StatsOptions> options, TimeProvider time, ILogger<StatsCollector> logger) : BackgroundService
{
    /// <summary>One collector leader per PostgreSQL cluster; the session lock is released if its API instance dies.</summary>
    private const long LeaderLockKey = 0x54_43_53_54_41_54_53; // "TCSTATS"
    private NodeSample? _previous;
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.ExporterUrl is null)
        {
            return;
        }
        await using var leadershipScope = scopes.CreateAsyncScope();
        var leadershipDb = leadershipScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await leadershipDb.Database.OpenConnectionAsync(stoppingToken);
        using var timer = new PeriodicTimer(settings.Interval, time);
        do
        {
            // A session advisory lock stays held across samples. Other replicas keep polling for the next leader,
            // but do not update their local previous sample or record accounting.
            var leader = await leadershipDb.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({LeaderLockKey}) AS \"Value\"")
                .SingleAsync(stoppingToken);
            if (leader)
            {
                await SampleOnceAsync(stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One sample; never throws for node or database failures.</summary>
    internal async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        NodeSample sample;
        try
        {
            sample = await client.GetAsync(settings.Node, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, settings.Node, ex.GetType().Name);
            return;
        }
        store.Set(sample);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<StatsRecorder>();
            await recorder.RecordRollupsAsync(_previous, sample, settings, cancellationToken);
            await recorder.RecordSessionsAsync(sample, cancellationToken);
            if (sample.At - _lastCleanup >= TimeSpan.FromHours(1))
            {
                await recorder.DeleteExpiredAsync(sample.At, settings, cancellationToken);
                _lastCleanup = sample.At;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex.GetType().Name);
        }
        _previous = sample;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stats of node {Node} unavailable ({ErrorType})")]
    private static partial void LogUnreachable(ILogger logger, string node, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording stats failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
