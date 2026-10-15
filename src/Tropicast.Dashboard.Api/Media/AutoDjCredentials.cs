using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Media;

/// <summary>
/// The station's AutoDJ source credential (#106): issued by the system when AutoDJ turns on, revoked when it turns
/// off or the station is deleted. Its secret is kept encrypted (Data Protection) so the playout worker can be given
/// it (#107); source auth checks only the hash, like any credential.
/// </summary>
internal static class AutoDjCredentials
{
    private const string Purpose = "Tropicast.AutoDj.Credential.v1";

    /// <summary>The active AutoDJ credential, issued now if there is none. Saved with the caller's changes.</summary>
    internal static async Task<BroadcastCredential> EnsureAsync(AppDbContext db, IDataProtectionProvider protection, Guid stationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await ActiveAsync(db, stationId, cancellationToken);
        if (active is not null)
        {
            return active;
        }
        var secret = Secrets.New();
        var credential = BroadcastCredential.CreateForAutoDj(stationId, Secrets.Hash(secret),
            protection.CreateProtector(Purpose).Protect(secret), now);
        db.BroadcastCredentials.Add(credential);
        return credential;
    }

    /// <summary>Revokes the active AutoDJ credential, if any (the playout worker is refused from its next connect).</summary>
    internal static async Task RevokeAsync(AppDbContext db, Guid stationId, DateTimeOffset now, CancellationToken cancellationToken)
        => (await ActiveAsync(db, stationId, cancellationToken))?.Revoke(now);

    /// <summary>The secret, or null when it cannot be decrypted (e.g. lost keys): the caller then rotates it.</summary>
    internal static string? Reveal(IDataProtectionProvider protection, BroadcastCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        try
        {
            return credential.ProtectedSecret is { } value ? protection.CreateProtector(Purpose).Unprotect(value) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static Task<BroadcastCredential?> ActiveAsync(AppDbContext db, Guid stationId, CancellationToken cancellationToken)
        => db.BroadcastCredentials.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .SingleOrDefaultAsync(c => c.StationId == stationId && c.Purpose == CredentialPurpose.AutoDj && c.RevokedAt == null,
                cancellationToken);
}
