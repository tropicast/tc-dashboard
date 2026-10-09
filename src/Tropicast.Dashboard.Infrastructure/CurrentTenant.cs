using Tropicast.Dashboard.Application;

namespace Tropicast.Dashboard.Infrastructure;

/// <summary>Per-scope tenant, set by authentication (#4) or by a background job before it queries.</summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public Guid? TenantId { get; set; }
}
