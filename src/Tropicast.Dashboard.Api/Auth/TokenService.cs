using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Infrastructure.Identity;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>Tokens for the desktop app: <see cref="AccessToken"/> lasts 15 minutes; refresh to get a new pair.</summary>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="AccessToken">Send as <c>Authorization: Bearer …</c>.</param>
/// <param name="ExpiresIn">Access token lifetime in seconds.</param>
/// <param name="RefreshToken">One-time: each refresh returns a new one. Revoke it to sign the device out.</param>
internal sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

/// <summary>
/// Issues short-lived access tokens with the Identity bearer-token format (validated by the bearer handler), bound to
/// a <see cref="DeviceSession"/> that holds the revocable refresh token.
/// </summary>
internal sealed class TokenService(SignInManager<AppUser> signIn, IOptionsMonitor<BearerTokenOptions> bearer, TimeProvider time)
{
    internal const string DeviceSessionClaim = "device_session";

    public async Task<TokenResponse> IssueAsync(AppUser user, DeviceSession session, string refreshToken)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(DeviceSessionClaim, session.Id.ToString()));
        var options = bearer.Get(IdentityConstants.BearerScheme);
        var now = time.GetUtcNow();
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + options.BearerTokenExpiration };
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.BearerScheme);
        return new TokenResponse("Bearer", options.BearerTokenProtector.Protect(ticket),
            (long)options.BearerTokenExpiration.TotalSeconds, refreshToken);
    }
}
