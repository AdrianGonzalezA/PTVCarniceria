using System.Globalization;
using System.Text.RegularExpressions;

namespace Carnicerias.Api.Catalog;

public sealed record KretzDecodedLabel(string Plu, decimal WeightKg, string Barcode);

/// <summary>Perfil explícito de laboratorio: 20 + PLU(5) + gramos(5) + control EAN13.</summary>
public static partial class KretzScaleLabel
{
    [GeneratedRegex("^QR:(.{23}):(\\d{2}):(\\d{13})$", RegexOptions.CultureInvariant)]
    private static partial Regex QrReferencePattern();

    public static bool TryDecode(string? reference, out KretzDecodedLabel decoded)
    {
        decoded = null!;
        if (string.IsNullOrEmpty(reference) || reference.Length > 80) return false;
        string barcode;
        if (reference.StartsWith("EAN:", StringComparison.Ordinal))
        {
            barcode = reference[4..];
        }
        else
        {
            var match = QrReferencePattern().Match(reference);
            if (!match.Success || !ValidHeader(match.Groups[1].Value, int.Parse(match.Groups[2].Value,
                    CultureInfo.InvariantCulture)))
                return false;
            barcode = match.Groups[3].Value;
        }

        if (barcode.Length != 13 || !barcode.StartsWith("20", StringComparison.Ordinal) ||
            !barcode.All(character => character is >= '0' and <= '9')) return false;
        var sum = 0;
        for (var index = 0; index < 12; index++)
            sum += (barcode[index] - '0') * (index % 2 == 0 ? 1 : 3);
        if ((10 - sum % 10) % 10 != barcode[12] - '0') return false;
        var grams = int.Parse(barcode.AsSpan(7, 5), CultureInfo.InvariantCulture);
        if (grams == 0) return false;
        decoded = new KretzDecodedLabel(barcode[2..7], grams / 1000m, barcode);
        return true;
    }

    private static bool ValidHeader(string header, int lineNumber)
    {
        if (header.Length != 23 || header[2] != '/' || header[5] != '/' ||
            header[10] != ':' ||
            !DateOnly.TryParseExact(header[..8], "dd/MM/yy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) ||
            !TimeOnly.TryParseExact(header.Substring(8, 5), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) ||
            !header.Substring(13, 4).All(char.IsAsciiDigit) ||
            !header.Substring(19, 4).All(char.IsAsciiDigit)) return false;
        var count = int.Parse(header.AsSpan(21, 2), CultureInfo.InvariantCulture);
        return count is >= 1 and <= 99 && lineNumber >= 1 && lineNumber <= count;
    }
}
