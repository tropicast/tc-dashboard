using System.Security.Cryptography;
using System.Text;

namespace Tropicast.Dashboard.Application.Security;

/// <summary>Random one-time secrets and the hashes stored in their place.</summary>
public static class Secrets
{
    /// <summary>256 random bits, base64url (43 characters).</summary>
    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lowercase hex SHA-256: what the database stores instead of the secret.</summary>
    public static string Hash(string secret)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>Compares a presented secret with a stored hash in constant time.</summary>
    public static bool Matches(string secret, string storedHash)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(storedHash));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
