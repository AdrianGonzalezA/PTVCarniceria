using Carnicerias.Domain.Inventory;

namespace Carnicerias.IntegrationTests;

public sealed class PieceBarcodeReadTests
{
    [Fact]
    public void ReadsPieceIdentityAndWeightFromAConfiguredEan13Profile()
    {
        var read = PieceBarcodeRead.Parse("prefijo(1) pro_identif(6) peso(5) control_ean13(1)",
            "pro_identif", "peso", 3, "2250661516008");

        Assert.Equal("250661", read.ExternalIdentifier);
        Assert.Equal(51.600m, read.WeightKg);
    }

    [Fact]
    public void SupportsADifferentIdentifierFieldWithoutChangingTheParser()
    {
        var read = PieceBarcodeRead.Parse("lote(2) ref(3) peso(5)",
            "ref", "peso", 3, "0112304500");

        Assert.Equal("123", read.ExternalIdentifier);
        Assert.Equal(4.500m, read.WeightKg);
    }

    [Fact]
    public void DoesNotAllowTheWeightFieldToIdentifyAPiece()
    {
        Assert.Throws<ArgumentException>(() => PieceBarcodeRead.Parse(
            "pro_identif(6) peso(5)", "peso", "peso", 3, "25066151600"));
    }

    [Theory]
    [InlineData("25066100000", 3)]
    [InlineData("25066100001", 4)]
    public void RejectsNonpositiveOrTooPreciseWeight(string code, int decimals)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PieceBarcodeRead.Parse(
            "pro_identif(6) peso(5)", "pro_identif", "peso", decimals, code));
    }
}
