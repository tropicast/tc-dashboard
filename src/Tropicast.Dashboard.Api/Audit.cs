using System.Security.Claims;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain.Audit;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api;

/// <summary>Adds an audit entry for the caller in the current tenant; saved with the change it describes.</summary>
internal static class Audit
{
    internal static void Record(AppDbContext db, TenantAccess access, ClaimsPrincipal user, string action, string targetType,
        string targetId, string? summary, DateTimeOffset now)
    {
        var actor = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        db.AuditEntries.Add(AuditEntry.Record(access.TenantId!.Value, actor, action, targetType, targetId, summary, now));
    }
}
