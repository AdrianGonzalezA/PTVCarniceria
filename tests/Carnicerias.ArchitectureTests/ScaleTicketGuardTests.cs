using Carnicerias.Api.Sales;

namespace Carnicerias.ArchitectureTests;

public sealed class ScaleTicketGuardTests
{
    private const string Header = "17/11/2613:390003 A0102";

    [Fact]
    public void CompleteQrProvidesOneTicketKey()
    {
        Assert.True(ScaleTicketGuard.TryGetCompleteTicketKeys([
            $"QR:{Header}:01:2000123007502",
            $"QR:{Header}:02:2000045005006",
            "EAN:2000123007502",
        ], out var keys));
        Assert.Equal([Header[..^2]], keys);
    }

    [Theory]
    [InlineData("QR:17/11/2613:390003 A0102:01:2000123007502")]
    [InlineData("QR:17/11/2613:390003 A0102:02:2000045005006")]
    public void PartialQrCannotBecomeASaleDraft(string onlyLine)
    {
        Assert.False(ScaleTicketGuard.TryGetCompleteTicketKeys([onlyLine], out _));
    }

    [Fact]
    public void RepeatedQrLineCannotBeSaved()
    {
        var line = $"QR:{Header}:01:2000123007502";
        Assert.False(ScaleTicketGuard.TryGetCompleteTicketKeys([
            line, line,
        ], out _));
    }

    [Fact]
    public void DifferentCountForSameTicketKeyIsRejected()
    {
        Assert.False(ScaleTicketGuard.TryGetCompleteTicketKeys([
            $"QR:{Header}:01:2000123007502",
            "QR:17/11/2613:390003 A0101:01:2000123007502",
        ], out _));
    }
}
