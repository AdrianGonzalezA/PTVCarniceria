using Carnicerias.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class PlatformAccessIdentityTests
{
    [Fact]
    public void CreateNormalizesUsernameAndEmailForUniqueness()
    {
        var user = UserIdentity.Create(
            "  Admin  ",
            " Admin@Example.COM ",
            "test-password-hash");

        Assert.Equal("Admin", user.Username);
        Assert.Equal("ADMIN", user.UsernameNormalized);
        Assert.Equal("Admin@Example.COM", user.Email);
        Assert.Equal("admin@example.com", user.EmailNormalized);
    }

    [Theory]
    [InlineData(" ", "admin@example.com", "hash")]
    [InlineData("admin", " ", "hash")]
    [InlineData("admin", "admin@example.com", " ")]
    public void CreateRejectsMissingIdentityOrSecurityData(
        string username,
        string email,
        string passwordHash)
    {
        Assert.Throws<ArgumentException>(() =>
            UserIdentity.Create(username, email, passwordHash));
    }
}
