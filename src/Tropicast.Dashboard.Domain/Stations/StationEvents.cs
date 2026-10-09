namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>A station was created: provisioning applies its limits (#8).</summary>
public sealed record StationCreated(Guid StationId, Guid TenantId, string PublicId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Station settings changed: RadioBrowser listings refresh (#13).</summary>
public sealed record StationChanged(Guid StationId, Guid TenantId, string PublicId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A station was deleted: provisioning removes its mounts, listings are withdrawn.</summary>
public sealed record StationDeleted(Guid StationId, Guid TenantId, string PublicId, DateTimeOffset OccurredAt) : IDomainEvent;
