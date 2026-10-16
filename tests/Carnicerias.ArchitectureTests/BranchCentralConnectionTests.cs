using Carnicerias.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchCentralConnectionTests
{
    private const string FirstToken = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string SecondToken = "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConnectionBeginsInactiveAndNeverRetainsPlaintextToken()
    {
        var connection = BranchCentralConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            "http://127.0.0.1:5249/", true, Guid.NewGuid(), Now);

        Assert.NotEqual(Guid.Empty, connection.InstallationId);
        Assert.False(connection.IsActive);
        Assert.False(connection.VerifyCredential(FirstToken, Now));
        connection.IssueCredential(FirstToken, Now);
        Assert.True(connection.IsActive);
        Assert.True(connection.VerifyCredential(FirstToken, Now));
        Assert.NotEqual(FirstToken, connection.CredentialSha256);
        Assert.Equal(64, connection.CredentialSha256?.Length);
    }

    [Fact]
    public void RotationKeepsInstallationAndExpiresPreviousCredential()
    {
        var connection = BranchCentralConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            "https://alta.example.test/", false, Guid.NewGuid(), Now);
        connection.IssueCredential(FirstToken, Now);
        var installationId = connection.InstallationId;

        connection.IssueCredential(SecondToken, Now.AddMinutes(1));

        Assert.Equal(installationId, connection.InstallationId);
        Assert.True(connection.VerifyCredential(FirstToken, Now.AddMinutes(30)));
        Assert.False(connection.VerifyCredential(FirstToken, Now.AddHours(2)));
        Assert.True(connection.VerifyCredential(SecondToken, Now.AddHours(2)));
        connection.CompleteRotation(Now.AddMinutes(2));
        Assert.False(connection.VerifyCredential(FirstToken, Now.AddMinutes(3)));
    }

    [Fact]
    public void DeactivationRevokesBothCredentialsAndReissueDoesNotRestoreOldOne()
    {
        var connection = BranchCentralConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            "https://alta.example.test/", false, Guid.NewGuid(), Now);
        connection.IssueCredential(FirstToken, Now);
        connection.Deactivate(Now.AddMinutes(1));

        Assert.False(connection.VerifyCredential(FirstToken, Now.AddMinutes(1)));
        connection.IssueCredential(SecondToken, Now.AddMinutes(2));
        Assert.False(connection.VerifyCredential(FirstToken, Now.AddMinutes(2)));
        Assert.True(connection.VerifyCredential(SecondToken, Now.AddMinutes(2)));
    }

    [Fact]
    public void InvalidTimestampDoesNotChangeCredentialState()
    {
        var connection = BranchCentralConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            "https://alta.example.test/", false, Guid.NewGuid(), Now);
        connection.IssueCredential(FirstToken, Now);
        var revision = connection.Revision;

        Assert.Throws<ArgumentException>(() => connection.IssueCredential(
            SecondToken, Now.ToOffset(TimeSpan.FromHours(-3))));

        Assert.Equal(revision, connection.Revision);
        Assert.True(connection.VerifyCredential(FirstToken, Now));
        Assert.False(connection.VerifyCredential(SecondToken, Now));
    }

    [Theory]
    [InlineData("http://192.168.1.20:5249/", true)]
    [InlineData("http://localhost:5249/", true)]
    [InlineData("http://127.0.0.1:5249/", false)]
    [InlineData("https://user:pass@alta.example.test/", false)]
    [InlineData("https://alta.example.test/path", false)]
    [InlineData("https://alta.example.test/?a=1", false)]
    public void RejectsUnsafeDestination(string url, bool allowLabLoopback)
    {
        Assert.Throws<ArgumentException>(() => BranchCentralConnection.Create(
            Guid.NewGuid(), Guid.NewGuid(), url, allowLabLoopback, Guid.NewGuid(), Now));
    }
}
