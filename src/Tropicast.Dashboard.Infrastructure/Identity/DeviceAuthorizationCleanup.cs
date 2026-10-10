using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>
/// Deletes expired desktop sign-in requests every hour, so the table stays small even when nobody signs in.
/// Starting a sign-in also deletes them.
/// </summary>
internal sealed partial class DeviceAuthorizationCleanup(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<DeviceAuthorizationCleanup> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
        return await db.DeviceAuthorizations.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Device sign-in cleanup failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
