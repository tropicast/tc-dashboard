namespace Tropicast.Dashboard.Infrastructure.Outbox;

/// <summary>A domain event stored in the same transaction as the change that raised it, delivered later.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }
    /// <summary>Event type name, e.g. <c>StationCreated</c>.</summary>
    public string Type { get; private set; } = null!;
    /// <summary>The event as JSON.</summary>
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    /// <summary>Exception type of the last failure; never the message, which may hold data.</summary>
    public string? LastError { get; private set; }

    internal static OutboxMessage From(string type, string payload, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        Type = type,
        Payload = payload,
        OccurredAt = occurredAt,
    };

    internal void Processed(DateTimeOffset now) => ProcessedAt = now;

    /// <summary>Exponential backoff, capped at one hour.</summary>
    internal void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error;
        NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Attempts) * 5));
    }
}
