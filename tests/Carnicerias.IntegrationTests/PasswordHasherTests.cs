using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashUsesRandomSaltAndVerifyAcceptsOnlyTheOriginalPassword()
    {
        var hasher = new Argon2idPasswordHasher();
        const string password = "correct horse battery staple";

        var firstHash = hasher.Hash(password);
        var secondHash = hasher.Hash(password);

        Assert.NotEqual(firstHash, secondHash);
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", firstHash, StringComparison.Ordinal);
        Assert.True(hasher.Verify(password, firstHash));
        Assert.False(hasher.Verify("incorrect", firstHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\t ")]
    public void HashRejectsAnEmptyPassword(string password)
    {
        Assert.Throws<ArgumentException>(() => new Argon2idPasswordHasher().Hash(password));
    }

    [Fact]
    public void HashRejectsPasswordsBeyondInputByteLimit()
    {
        var password = new string('a', 1025);

        Assert.Throws<ArgumentException>(() => new Argon2idPasswordHasher().Hash(password));
        Assert.False(new Argon2idPasswordHasher().Verify(password, "$argon2id$invalid"));
    }
}
