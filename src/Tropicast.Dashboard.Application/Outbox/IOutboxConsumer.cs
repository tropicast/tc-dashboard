namespace Tropicast.Dashboard.Application.Outbox;

/// <summary>A delivered domain event: its type name and JSON payload.</summary>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt);

/// <summary>
/// Receives domain events from the outbox (provisioning #8, RadioBrowser #13). Delivery is at least once:
/// handlers must be idempotent. A throw retries the message later with backoff. Messages whose type no consumer
/// handles stay stored until one does, so a consumer added later receives earlier events too.
/// </summary>
public interface IOutboxConsumer
{
    /// <summary>Event type names this consumer handles, e.g. <c>StationCreated</c>.</summary>
    IReadOnlyCollection<string> EventTypes { get; }
    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}
