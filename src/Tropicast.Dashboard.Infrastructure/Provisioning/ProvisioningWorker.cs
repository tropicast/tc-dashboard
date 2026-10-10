using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Outbox;

namespace Tropicast.Dashboard.Infrastructure.Provisioning;

/// <summary>Wakes the worker early (station or plan change, operator request).</summary>
public sealed class ProvisioningSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => _channel.Writer.TryWrite(true);

    internal async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timer.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Interval elapsed.
        }
    }
}

/// <summary>Station and plan events from the outbox wake provisioning.</summary>
internal sealed class ProvisioningOutboxConsumer(ProvisioningSignal signal) : IOutboxConsumer
{
    public IReadOnlyCollection<string> EventTypes { get; } = ["StationCreated", "StationDeleted", "TenantPlanChanged"];

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        signal.Wake();
        return Task.CompletedTask;
    }
}

/// <summary>Reconciles on every wake-up and at least every <see cref="ProvisioningOptions.Interval"/>.</summary>
internal sealed partial class ProvisioningWorker(IServiceScopeFactory scopes, ProvisioningSignal signal,
    IOptions<ProvisioningOptions> options, ILogger<ProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Reconciler>().ReconcileAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
            await signal.WaitAsync(options.Value.Interval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning pass failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
