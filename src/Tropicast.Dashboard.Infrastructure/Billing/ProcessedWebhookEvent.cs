namespace Tropicast.Dashboard.Infrastructure.Billing;

/// <summary>A provider webhook event already applied: a replay (or a retry after success) changes nothing.</summary>
public sealed class ProcessedWebhookEvent
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private ProcessedWebhookEvent()
    {
    }

    public string Id { get; private set; } = null!;
    public string Type { get; private set; } = null!;
    public DateTimeOffset ProcessedAt { get; private set; }

    internal static ProcessedWebhookEvent From(string id, string type, DateTimeOffset now) => new()
    {
        Id = id.Length <= 255 ? id : throw new ArgumentException("At most 255 characters.", nameof(id)),
        Type = type.Length <= 128 ? type : type[..128],
        ProcessedAt = now,
    };
}
