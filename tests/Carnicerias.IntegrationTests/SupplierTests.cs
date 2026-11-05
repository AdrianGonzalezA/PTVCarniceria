using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class SupplierTests
{
    [Fact]
    public void KeepsAnEditableCompanyScopedIdentityWithoutChangingExistingSnapshots()
    {
        var companyId = Guid.NewGuid();
        var supplier = new Supplier(companyId, " FRIGO-1 ", " Frigorífico Norte ", " 30710106513 ");
        var original = (supplier.Code, supplier.Name, supplier.Identification);

        Assert.Equal(companyId, supplier.CompanyId);
        Assert.Equal("FRIGO-1", supplier.NormalizedCode);
        Assert.Equal("Frigorífico Norte", supplier.Name);
        Assert.True(supplier.IsActive);
        supplier.UpdateDetails("frigo-2", "Nuevo nombre", null);
        supplier.Deactivate();

        Assert.Equal(("FRIGO-1", "Frigorífico Norte", "30710106513"), original);
        Assert.Equal("FRIGO-2", supplier.NormalizedCode);
        Assert.Null(supplier.Identification);
        Assert.False(supplier.IsActive);
    }

    [Fact]
    public void RejectsMissingIdentityOrOversizedFields()
    {
        Assert.Throws<ArgumentException>(() => new Supplier(Guid.Empty, "A", "Proveedor"));
        Assert.Throws<ArgumentException>(() => new Supplier(Guid.NewGuid(), " ", "Proveedor"));
        Assert.Throws<ArgumentException>(() => new Supplier(Guid.NewGuid(), "A", new string('x', 201)));
        Assert.Throws<ArgumentException>(() => new Supplier(Guid.NewGuid(), "A", "Proveedor",
            new string('x', 41)));
    }

    [Fact]
    public void ModelEnforcesUniqueCompanySupplierCode()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=design_only").Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Supplier));

        Assert.NotNull(entity);
        Assert.Equal("suppliers", entity.GetTableName());
        Assert.Equal("inventory", entity.GetSchema());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(Supplier.CompanyId), nameof(Supplier.NormalizedCode)]));
    }
}
