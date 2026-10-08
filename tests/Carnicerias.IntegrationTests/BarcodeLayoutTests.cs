using Carnicerias.Domain.Inventory;

namespace Carnicerias.IntegrationTests;

public sealed class BarcodeLayoutTests
{
    [Fact]
    public void DecodesNamedFieldsUsingConfiguredLengths()
    {
        var layout = BarcodeLayout.Parse("pro_numero(5) pro_item(3) peso(4)");

        var result = layout.Decode("123450070245");

        Assert.Equal("12345", result.Field("pro_numero"));
        Assert.Equal("007", result.Field("pro_item"));
        Assert.Equal("0245", result.Field("peso"));
        Assert.Equal(2.45m, result.ScaledDecimal("peso", 2));
    }

    [Fact]
    public void UsesTheFieldOrderAndScaleOfEachProfile()
    {
        var layout = BarcodeLayout.Parse("peso(5) pro_item(2) pro_numero(4)");

        var result = layout.Decode("01234071234");

        Assert.Equal(1.234m, result.ScaledDecimal("peso", 3));
        Assert.Equal("07", result.Field("pro_item"));
        Assert.Equal("1234", result.Field("pro_numero"));
    }

    [Fact]
    public void AcceptsSpacesBetweenFieldNamesAndWidthsAsInTheProposedFormula()
    {
        var layout = BarcodeLayout.Parse("pro_numero(5) pro_item (3) peso (4)");

        Assert.Equal("007", layout.Decode("123450070245").Field("pro_item"));
        Assert.Equal(12, layout.Length);
    }

    [Fact]
    public void ProposedCycleTwoExampleSeparatesProductIdentifierAnd51Kilos600Grams()
    {
        var layout = BarcodeLayout.Parse("pro_identif(6) peso(5)");

        var read = layout.Decode("25066151600");

        Assert.Equal("250661", read.Field("PRO_IDENTIF"));
        Assert.Equal("51600", read.Field("peso"));
        Assert.Equal(51.600m, read.ScaledDecimal("peso", 3));
    }

    [Fact]
    public void Ean13ExampleValidatesCheckDigitAndPreservesPieceWeight()
    {
        var layout = BarcodeLayout.Parse("prefijo(1) pro_identif(6) peso(5) control_ean13(1)");

        var read = layout.Decode("2250661516008");

        Assert.Equal(13, layout.Length);
        Assert.Equal("2", read.Field("prefijo"));
        Assert.Equal("250661", read.Field("pro_identif"));
        Assert.Equal(51.600m, read.ScaledDecimal("peso", 3));
        Assert.Equal("8", read.Field("control_ean13"));
    }

    [Theory]
    [InlineData("2250661516007")]
    [InlineData("225066151600A")]
    public void Ean13ExampleRejectsInvalidCheckDigitOrNonnumericCode(string code)
    {
        var layout = BarcodeLayout.Parse("prefijo(1) pro_identif(6) peso(5) control_ean13(1)");

        Assert.Throws<FormatException>(() => layout.Decode(code));
    }

    [Theory]
    [InlineData("control_ean13(1) pro_identif(6) peso(6)")]
    [InlineData("prefijo(1) pro_identif(6) peso(4) control_ean13(2)")]
    [InlineData("pro_identif(6) peso(5) control_ean13(1)")]
    public void Ean13ControlFieldRequiresFinalDigitAndThirteenDigits(string formula)
    {
        Assert.Throws<ArgumentException>(() => BarcodeLayout.Parse(formula));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pro_numero(0) peso(4)")]
    [InlineData("pro_numero(5) pro_numero(3)")]
    [InlineData("pro_numero(5) basura")]
    [InlineData("pro_numero(81)")]
    public void RejectsInvalidFormulas(string formula)
    {
        Assert.Throws<ArgumentException>(() => BarcodeLayout.Parse(formula));
    }

    [Theory]
    [InlineData("12345007024")]
    [InlineData("1234500702459")]
    public void RejectsCodesWithTheWrongLength(string code)
    {
        var layout = BarcodeLayout.Parse("pro_numero(5) pro_item(3) peso(4)");

        Assert.Throws<FormatException>(() => layout.Decode(code));
    }

    [Fact]
    public void RejectsNonnumericWeightWithoutDiscardingOtherFields()
    {
        var layout = BarcodeLayout.Parse("pro_numero(5) pro_item(3) peso(4)");
        var result = layout.Decode("12345007A245");

        Assert.Equal("12345", result.Field("pro_numero"));
        Assert.Throws<FormatException>(() => result.ScaledDecimal("peso", 2));
    }

    [Fact]
    public void RejectsADecimalScaleOutsideTheFieldWidth()
    {
        var layout = BarcodeLayout.Parse("peso(4)");
        var result = layout.Decode("0245");

        Assert.Throws<ArgumentOutOfRangeException>(() => result.ScaledDecimal("peso", 5));
    }

    [Fact]
    public void RejectsAWeightThatExceedsDecimalRange()
    {
        var layout = BarcodeLayout.Parse("peso(40)");
        var result = layout.Decode(new string('9', 40));

        Assert.Throws<FormatException>(() => result.ScaledDecimal("peso", 2));
    }
}
