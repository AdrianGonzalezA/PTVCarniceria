using Carnicerias.Api.Inventory;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Carnicerias.ArchitectureTests;

public sealed class ShiftClosingReportTests
{
    private static ShiftClosingSnapshot Example() => new(1, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Carnicería & Cía", "Centro <Norte>",
        "Caja A", "cajero1", DateTimeOffset.Parse("2026-11-05T10:00:00Z", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-11-05T18:00:00Z", CultureInfo.InvariantCulture), 100m, 400m, 200m, 50m,
        650m, 80m, 25m, 10m, 20m, 560m, 550m, 500m, -10m,
        [new ShiftClosingPayment("cash", 480m), new ShiftClosingPayment("debit", 200m)]);

    [Fact]
    public void SnapshotRoundTripPreservesCountAndBreakdown()
    {
        var original = Example();
        var json = JsonSerializer.Serialize(original, ShiftClosingSnapshot.JsonOptions);
        var restored = JsonSerializer.Deserialize<ShiftClosingSnapshot>(json,
            ShiftClosingSnapshot.JsonOptions);
        Assert.NotNull(restored);
        Assert.Equal(original.ShiftId, restored.ShiftId);
        Assert.Equal(550m, restored.CountedCash);
        Assert.Equal(-10m, restored.CashDifference);
        Assert.Equal(2, restored.PaymentBreakdown.Count);
        Assert.True(restored.IsComplete);
    }

    [Fact]
    public async Task PdfUsesStoredSnapshotAndEscapesNames()
    {
        var report = Example();
        var html = ShiftClosingPdf.ToHtml(report);
        Assert.Contains("&amp;", html);
        Assert.Contains("Centro &lt;Norte&gt;", html);
        Assert.DoesNotContain("Centro <Norte>", html);
        Assert.Contains("550,00", html);
        Assert.Contains("-10,00", html);
        var pdf = await ShiftClosingPdf.RenderAsync(report);
        Assert.True(pdf.Length > 500);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
