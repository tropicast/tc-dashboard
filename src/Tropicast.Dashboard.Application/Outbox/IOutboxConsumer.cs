namespace Tropicast.Dashboard.Application.Outbox;

/// <summary>A delivered domain event: its type name and JSON payload.</summary>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt);

/// <summary>
/// Receives domain events from the outbox (provisioning #8, RadioBrowser #13). Delivery is at least once:
/// handlers must be idempotent. A throw retries the message later with backoff.
/// </summary>
public interface IOutboxConsumer
{
    bool Accepts(string eventType);
    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}
