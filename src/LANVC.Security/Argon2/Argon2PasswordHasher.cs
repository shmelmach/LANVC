using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace LANVC.Security.Argon2;

public static class Argon2PasswordHasher
{
    public const int SaltLength = 16;
    public const int HashLength = 32;
    public const int Iterations = 3;
    public const int MemorySize = 65536; // 64 MB
    public const int DegreeOfParallelism = 4;

    public static (string HashBase64, string SaltHex) HashPassword(string password)
    {
        byte[] salt = new byte[SaltLength];
        RandomNumberGenerator.Fill(salt);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };

        byte[] hash = argon2.GetBytes(HashLength);
        return (Convert.ToBase64String(hash), Convert.ToHexString(salt));
    }

    public static bool VerifyPassword(string password, string hashBase64, string saltHex)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashBase64) || string.IsNullOrEmpty(saltHex))
            return false;

        try
        {
            byte[] salt = Convert.FromHexString(saltHex);
            byte[] expectedHash = Convert.FromBase64String(hashBase64);

            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = DegreeOfParallelism,
                MemorySize = MemorySize,
                Iterations = Iterations
            };

            byte[] actualHash = argon2.GetBytes(expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }
}
