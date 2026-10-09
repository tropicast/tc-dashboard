namespace Tropicast.Dashboard.Domain.Audit;

/// <summary>
/// A security-relevant action in a tenant: who did what to which object, and when. Never holds secrets.
/// </summary>
public sealed class AuditEntry : ITenantOwned
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>The user who acted; null for the system (e.g. a background job).</summary>
    public Guid? ActorUserId { get; private set; }
    /// <summary>Dotted action name, e.g. <c>credential.created</c>.</summary>
    public string Action { get; private set; } = null!;
    /// <summary>Kind of object acted on, e.g. <c>BroadcastCredential</c>.</summary>
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    /// <summary>Short non-secret context, e.g. the device label.</summary>
    public string? Summary { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditEntry Record(Guid tenantId, Guid? actorUserId, string action, string targetType, string targetId,
        string? summary, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        TenantId = tenantId,
        ActorUserId = actorUserId,
        Action = Text.Required(action, 64, nameof(action)),
        TargetType = Text.Required(targetType, 64, nameof(targetType)),
        TargetId = Text.Required(targetId, 64, nameof(targetId)),
        Summary = string.IsNullOrWhiteSpace(summary) ? null : Text.Required(summary, 256, nameof(summary)),
        OccurredAt = now,
    };
}
