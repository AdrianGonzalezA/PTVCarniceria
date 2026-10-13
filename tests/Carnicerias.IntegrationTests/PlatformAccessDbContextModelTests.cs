using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carnicerias.IntegrationTests;

public sealed class PlatformAccessDbContextModelTests
{
    [Fact]
    public void CompanyAccessCodeIsUniqueAndUserCredentialsAreScoped()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        var company = db.Model.FindEntityType(typeof(Company));
        var user = db.Model.FindEntityType(typeof(UserIdentity));

        Assert.NotNull(company);
        Assert.NotNull(user);
        Assert.Contains(company.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Company.Code)]));
        Assert.Contains(user.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(UserIdentity.CompanyId), nameof(UserIdentity.UsernameNormalized)]));
        Assert.Contains(user.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(UserIdentity.CompanyId), nameof(UserIdentity.EmailNormalized)]));
        Assert.Contains(user.GetForeignKeys(), key =>
            key.Properties.Select(property => property.Name).SequenceEqual([nameof(UserIdentity.CompanyId)]) &&
            key.PrincipalEntityType.ClrType == typeof(Company));
    }

    [Fact]
    public void SessionTokensAreStoredAsUniqueHashes()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        var session = db.Model.FindEntityType(typeof(UserSession));

        Assert.NotNull(session);
        Assert.Equal("sessions", session.GetTableName());
        Assert.Contains(session.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(UserSession.TokenHash)]));
    }

    [Fact]
    public void SessionMayBeBoundToCompanyBeforeBranchSelection()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);
        var session = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserSession));

        Assert.NotNull(session);
        Assert.Contains(session.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_sessions_context_pair" &&
            constraint.Sql == "\"BranchId\" IS NULL OR \"CompanyId\" IS NOT NULL");
    }

    [Fact]
    public void PriceListAssignmentsMatchTheCompanyOfBothBranchAndList()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        var assignment = db.Model.FindEntityType(typeof(BranchPriceList));

        Assert.NotNull(assignment);
        Assert.Equal("catalog_pricing", assignment.GetSchema());
        Assert.Contains(assignment.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(Branch) &&
            key.Properties.Select(property => property.Name).SequenceEqual([
                nameof(BranchPriceList.CompanyId), nameof(BranchPriceList.BranchId)]));
        Assert.Contains(assignment.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(PriceList) &&
            key.Properties.Select(property => property.Name).SequenceEqual([
                nameof(BranchPriceList.CompanyId), nameof(BranchPriceList.PriceListId)]));
    }

    [Fact]
    public void CustomerCodesAreUniquePerCompanyWithoutDatabaseConnection()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        var customer = db.Model.FindEntityType(typeof(CustomerAccount));

        Assert.NotNull(customer);
        Assert.Equal("customers_credit", customer.GetSchema());
        Assert.Contains(customer.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(CustomerAccount.CompanyId), nameof(CustomerAccount.NormalizedCode)]));
        Assert.Contains(customer.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(Company));
    }
}
