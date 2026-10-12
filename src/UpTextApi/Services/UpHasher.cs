using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace UpTextApi.Services;

internal static class UpHasher
{
    private const int MemorySize = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const string Prefix = "$argon2id$v=19$m=19456,t=2,p=1$";

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(password, salt);
        return Prefix + Encode(salt) + "$" + Encode(hash);
    }

    public static bool VerifyPassword(string? password, string? encodedHash)
    {
        // Accept only this supported profile, bounding CPU/memory even for corrupt stored hashes.
        if (password is null || encodedHash is null || encodedHash.Length > 128 ||
            !encodedHash.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = encodedHash[Prefix.Length..].Split('$');
        if (parts.Length != 2 || parts[0].Length != 22 || parts[1].Length != 43)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0] + "==");
            var expected = Convert.FromBase64String(parts[1] + "=");
            if (salt.Length != 16 || expected.Length != 32)
                return false;
            return CryptographicOperations.FixedTimeEquals(Derive(password, salt), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static byte[] Derive(string password, byte[] salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(bytes)
            {
                Salt = salt,
                MemorySize = MemorySize,
                Iterations = Iterations,
                DegreeOfParallelism = Parallelism
            };
            return argon2.GetBytes(32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
