using System.Security.Cryptography;

namespace StoreManager.App.Services;

internal sealed class PasswordHasher
{
    public const int CurrentIterations = 310_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public (byte[] Hash, byte[] Salt, string Parameters) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        return (Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, HashAlgorithmName.SHA256, HashLength), salt, $"PBKDF2-SHA256;iterations={CurrentIterations};dk={HashLength}");
    }

    public bool Verify(string password, byte[] expectedHash, byte[] salt, string parameters)
    {
        var parsed = ParseIterations(parameters);
        if (salt.Length < 16 || expectedHash.Length != HashLength || parsed is < 100_000 or > 2_000_000) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, parsed, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }

    private static int ParseIterations(string parameters)
    {
        const string marker = "iterations=";
        var start = parameters.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 0;
        start += marker.Length;
        var end = parameters.IndexOf(';', start);
        return int.TryParse(end < 0 ? parameters[start..] : parameters[start..end], out var iterations) ? iterations : 0;
    }
}
