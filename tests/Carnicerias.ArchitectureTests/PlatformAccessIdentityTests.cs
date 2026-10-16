using Carnicerias.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class PlatformAccessIdentityTests
{
    [Fact]
    public void CompanyCodeIsNormalizedAndDoesNotChangeWithDisplayName()
    {
        var company = new Company("Empresa Visual", "  visual-pos  ");

        company.Rename("Otro nombre");

        Assert.Equal("VISUAL-POS", company.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("con espacios")]
    [InlineData("á")]
    public void CompanyRejectsInvalidAccessCode(string code)
    {
        Assert.Throws<ArgumentException>(() => new Company("Empresa", code));
    }

    [Fact]
    public void ClientIdentityBelongsToExactlyOneCompany()
    {
        var companyId = Guid.NewGuid();
        var user = UserIdentity.CreateForCompany(companyId, "cajero1", "cajero@example.test", "hash");

        Assert.Equal(companyId, user.CompanyId);
        Assert.False(user.IsManufacturer);
    }

    [Fact]
    public void ManufacturerIdentityHasNoClientCompany()
    {
        var user = UserIdentity.CreateManufacturer("fabricante", "fabricante@example.test", "hash");

        Assert.Null(user.CompanyId);
        Assert.True(user.IsManufacturer);
    }

    [Fact]
    public void ClientIdentityRejectsEmptyCompanyId()
    {
        Assert.Throws<ArgumentException>(() => UserIdentity.CreateForCompany(
            Guid.Empty, "cajero1", "cajero@example.test", "hash"));
    }

    [Theory]
    [InlineData("cashier@example.test", "other@example.test")]
    [InlineData("cashier", "not-an-email")]
    public void IdentityRejectsAmbiguousCredentialKinds(string username, string email)
    {
        Assert.Throws<ArgumentException>(() => UserIdentity.CreateForCompany(
            Guid.NewGuid(), username, email, "hash"));
    }

    [Fact]
    public void SessionCannotChangeCompanyAfterLogin()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(Guid.NewGuid(), new string('a', 64), now, now.AddHours(8));
        var firstCompanyId = Guid.NewGuid();

        session.BindToCompany(firstCompanyId);
        session.SelectOperationalContext(firstCompanyId, Guid.NewGuid());
        session.ClearOperationalContext();

        Assert.Equal(firstCompanyId, session.CompanyId);
        Assert.Null(session.BranchId);
        Assert.Throws<InvalidOperationException>(() =>
            session.SelectOperationalContext(Guid.NewGuid(), Guid.NewGuid()));
    }

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
