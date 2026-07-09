using System.Security.Cryptography;

namespace Abis.Api.Security;

/// <summary>
/// PBKDF2 (HMAC-SHA256) password hashing for the ABIS-owned credential store — dependency-free
/// (no ASP.NET Identity package), using the framework's <see cref="Rfc2898DeriveBytes"/>.
/// <para>
/// The encoded form stored in <c>abis_user_credential.password_hash</c> is self-describing:
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c> — so the iteration
/// count and per-user random salt travel with the hash and can be raised later without a schema
/// change. Verification is constant-time.
/// </para>
/// This never stores or logs the plaintext; only the derived hash is persisted.
/// </summary>
public static class PasswordHashing
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 210_000;   // OWASP 2023 guidance for PBKDF2-HMAC-SHA256
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Derives a salted PBKDF2 hash and returns the self-describing encoded string.</summary>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time verify of a plaintext against a stored encoded hash. False on any
    /// malformed/blank/null input (never throws for bad stored data).</summary>
    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        if (salt.Length == 0 || expected.Length == 0) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
