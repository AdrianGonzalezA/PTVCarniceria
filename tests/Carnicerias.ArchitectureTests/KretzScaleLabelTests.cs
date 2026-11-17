using Carnicerias.Api.Catalog;

namespace Carnicerias.ArchitectureTests;

public sealed class KretzScaleLabelTests
{
    [Theory]
    [InlineData("EAN:2000123007502", "00123", 0.750)]
    [InlineData("QR:17/11/2613:390003 A0102:01:2000123007502", "00123", 0.750)]
    [InlineData("QR:17/11/2613:390003 A0102:02:2000045005006", "00045", 0.500)]
    public void DecodesCheckedWeightLabel(string reference, string plu, decimal expectedKg)
    {
        Assert.True(KretzScaleLabel.TryDecode(reference, out var decoded));
        Assert.Equal(plu, decoded.Plu);
        Assert.Equal(expectedKg, decoded.WeightKg);
    }

    [Theory]
    [InlineData("EAN:2000123007504")]
    [InlineData("QR:17/11/2613:390003 A0102:03:2000123007502")]
    [InlineData("QR:17/11/2613:390003 A0102:01:2000123007504")]
    [InlineData("EAN:2100123007504")]
    public void RejectsUntrustedOrInconsistentLabel(string reference) =>
        Assert.False(KretzScaleLabel.TryDecode(reference, out _));
}
