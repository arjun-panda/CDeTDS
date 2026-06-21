using CDeTDS.DAL;

namespace CDeTDS.Tests;

/// <summary>
/// Locks the password hashing behaviour (audit finding M-1): new hashes are salted
/// PBKDF2; legacy unsalted SHA-256 hashes still verify (so existing users can log in)
/// and are flagged for upgrade.
/// </summary>
public class PasswordHashTests
{
    [Fact]
    public void HashPassword_IsPbkdf2Format_AndSalted()
    {
        var h1 = Database.HashPassword("Secret@123");
        var h2 = Database.HashPassword("Secret@123");
        Assert.StartsWith("pbkdf2$", h1);
        Assert.NotEqual(h1, h2);            // random salt → different each time
        Assert.False(Database.IsLegacyHash(h1));
    }

    [Fact]
    public void VerifyPassword_RoundTrip()
    {
        var h = Database.HashPassword("Correct Horse 9!");
        Assert.True(Database.VerifyPassword("Correct Horse 9!", h));
        Assert.False(Database.VerifyPassword("wrong", h));
        Assert.False(Database.VerifyPassword("", h));
    }

    [Fact]
    public void VerifyPassword_AcceptsLegacyUnsaltedSha256()
    {
        // Legacy hash = uppercase hex of SHA-256(pw), as the old HashPassword produced.
        var pw = "admin@123";
        var legacy = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pw)));
        Assert.True(Database.VerifyPassword(pw, legacy));
        Assert.False(Database.VerifyPassword("nope", legacy));
        Assert.True(Database.IsLegacyHash(legacy));   // flagged for upgrade
    }
}
