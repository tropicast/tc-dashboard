using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class OutboxTests(PostgresContainer postgres) : IAsyncLifetime
{
    private readonly RecordingConsumer _consumer = new();
    private DashboardFactory _factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new DashboardFactory(postgres);
        await _factory.InitializeAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Messages_reach_consumers_once_and_failures_retry_with_backoff()
    {
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IOutboxConsumer>(_consumer)));
        var owner = new Browser(_factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await owner.CreateTenantAsync();
        await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });

        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        _consumer.FailNext = true;
        await dispatcher.DispatchBatchAsync(Token);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var failed = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.SingleAsync(Token);
            Assert.Equal((1, "InvalidOperationException"), (failed.Attempts, failed.LastError));
            Assert.Null(failed.ProcessedAt);
            Assert.NotNull(failed.NextAttemptAt);
        }

        // Backoff: not due yet.
        await dispatcher.DispatchBatchAsync(Token);
        Assert.Empty(_consumer.Received);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE outbox_messages SET next_attempt_at = now() - interval '1 second'", Token);
        }
        await dispatcher.DispatchBatchAsync(Token);
        await dispatcher.DispatchBatchAsync(Token);
        Assert.Equal("StationCreated", Assert.Single(_consumer.Received).Type);
    }

    private sealed class RecordingConsumer : IOutboxConsumer
    {
        public List<OutboxEnvelope> Received { get; } = [];
        public bool FailNext { get; set; }

        public bool Accepts(string eventType) => eventType == "StationCreated";

        public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Consumer down: secret details must not be stored.");
            }
            Received.Add(message);
            return Task.CompletedTask;
        }
    }
}
