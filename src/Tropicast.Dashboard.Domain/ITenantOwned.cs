namespace Tropicast.Dashboard.Domain;

/// <summary>Data that belongs to one tenant. Persistence filters it by the current tenant.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
