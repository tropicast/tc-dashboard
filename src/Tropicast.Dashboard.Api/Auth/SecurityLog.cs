using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>Account security events (<see cref="SecurityEvent"/>): saved with the change they describe.</summary>
internal static class SecurityLog
{
    internal const string SignedIn = "signin.succeeded";
    internal const string SignInFailed = "signin.failed";
    internal const string DeviceSignedIn = "device.signed_in";
    internal const string DeviceApproved = "device.approved";
    internal const string DeviceSignedOut = "device.signed_out";
    internal const string TokenReuse = "device.token_reuse";
    internal const string PasswordChanged = "password.changed";
    internal const string PasswordReset = "password.reset";

    internal static void Record(AppDbContext db, Guid userId, string action, HttpContext context, string? detail, DateTimeOffset now)
        => db.SecurityEvents.Add(SecurityEvent.Record(userId, action, context.Connection.RemoteIpAddress?.ToString(), detail, now));
}
