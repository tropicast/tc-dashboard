namespace Tropicast.Dashboard.Application;

/// <summary>The tenant of the current request or job. Null means none: tenant-owned queries then return nothing.</summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }
}
