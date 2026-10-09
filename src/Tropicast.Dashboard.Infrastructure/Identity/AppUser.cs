using Microsoft.AspNetCore.Identity;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>An account. Tenant roles are <see cref="Domain.Tenants.Membership"/> rows, not Identity roles.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public DateTimeOffset CreatedAt { get; set; }
}
