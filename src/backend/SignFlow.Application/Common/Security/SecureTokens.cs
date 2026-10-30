using System.Security.Cryptography;
using System.Text;

namespace SignFlow.Application.Common.Security;

/// <summary>
/// Secrets handed to people (session cookies, invitation and signing links): 32 random bytes, base64url, stored only
/// as their SHA-256 so a database copy cannot be used to act as anyone.
/// </summary>
public static class SecureTokens
{
    public static string Generate() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lowercase hex SHA-256 (64 characters).</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
