using System.Net;
using LANVC.Security.Argon2;
using LANVC.Security.Certificates;
using LANVC.Security.RateLimiting;
using Xunit;

namespace LANVC.Tests;

public class SecurityTests
{
    [Fact]
    public void Argon2PasswordHasher_HashesAndVerifiesPasswordCorrectly()
    {
        string originalPassword = "SecretLanGamingPassword@2026!";
        var (hash, salt) = Argon2PasswordHasher.HashPassword(originalPassword);

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.False(string.IsNullOrEmpty(salt));

        // Correct password must verify
        bool isValid = Argon2PasswordHasher.VerifyPassword(originalPassword, hash, salt);
        Assert.True(isValid);

        // Incorrect password must fail
        bool isInvalid = Argon2PasswordHasher.VerifyPassword("WrongPassword123", hash, salt);
        Assert.False(isInvalid);
    }

    [Fact]
    public void CertificateManager_GeneratesCertAndValidatesFingerprint()
    {
        using var cert = CertificateManager.GenerateServerCertificate("Test-LANVC-Server");
        string fingerprint = CertificateManager.GetSha256Fingerprint(cert);

        Assert.StartsWith("SHA256:", fingerprint);
        Assert.True(CertificateManager.ValidateFingerprint(cert, fingerprint));

        // Mismatched fingerprint must fail (MITM defense)
        string fakeFingerprint = "SHA256:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF";
        Assert.False(CertificateManager.ValidateFingerprint(cert, fakeFingerprint));
    }

    [Fact]
    public void TokenBucketRateLimiter_BansIpAfterExcessiveFailedAttempts()
    {
        var limiter = new TokenBucketRateLimiter(maxFailedAttempts: 3, windowMinutes: 1);
        var clientIp = IPAddress.Parse("198.51.100.42");

        Assert.False(limiter.IsBanned(clientIp, out _));

        limiter.RecordFailure(clientIp);
        Assert.False(limiter.IsBanned(clientIp, out _));

        limiter.RecordFailure(clientIp);
        Assert.False(limiter.IsBanned(clientIp, out _));

        // 3rd failure triggers ban
        limiter.RecordFailure(clientIp);
        Assert.True(limiter.IsBanned(clientIp, out var banTime));
        Assert.True(banTime > TimeSpan.Zero);

        // Success resets record
        limiter.RecordSuccess(clientIp);
        Assert.False(limiter.IsBanned(clientIp, out _));
    }
}
