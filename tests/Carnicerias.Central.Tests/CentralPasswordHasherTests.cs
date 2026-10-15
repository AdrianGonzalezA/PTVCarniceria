using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralPasswordHasherTests
{
    [Fact]
    public void SaltedHashVerifiesOnlyItsPassword()
    {
        var hasher = new CentralPasswordHasher();
        var first = hasher.Hash("a unique laboratory password");
        var second = hasher.Hash("a unique laboratory password");

        Assert.StartsWith("$argon2id$", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("a unique laboratory password", first));
        Assert.False(hasher.Verify("a different password", first));
        Assert.False(hasher.Verify("a unique laboratory password", "$argon2id$invalid"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void RejectsWeakInputAtBootstrap(string password)
    {
        Assert.Throws<ArgumentException>(() => new CentralPasswordHasher().Hash(password));
    }

    [Fact]
    public void SixCharacterPasswordIsAllowedOnlyWithTheVisualLabPolicy()
    {
        Assert.Throws<ArgumentException>(() => new CentralPasswordHasher().Hash("short1"));

        var visualHasher = new CentralPasswordHasher(6);
        var hash = visualHasher.Hash("short1");

        Assert.True(visualHasher.Verify("short1", hash));
        Assert.StartsWith("$argon2id$", hash, StringComparison.Ordinal);
    }
}
