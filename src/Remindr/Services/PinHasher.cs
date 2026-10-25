using System.Security.Cryptography;

namespace Remindr.Services;

/// <summary>PBKDF2-SHA256 PIN hashing. A 4-digit PIN is a privacy screen, not strong security.</summary>
public static class PinHasher
{
    public const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(c => c is >= '0' and <= '9');

    /// <summary>Returns base64 hash and base64 salt. Throws if the PIN is not exactly 4 digits.</summary>
    public static (string Hash, string Salt) Hash(string pin)
    {
        if (!IsValidPin(pin)) throw new ArgumentException("PIN must be exactly 4 digits.", nameof(pin));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(pin, salt);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string? pin, string? hashB64, string? saltB64)
    {
        if (!IsValidPin(pin) || string.IsNullOrEmpty(hashB64) || string.IsNullOrEmpty(saltB64)) return false;
        try
        {
            var expected = Convert.FromBase64String(hashB64);
            var actual = Derive(pin!, Convert.FromBase64String(saltB64));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}
