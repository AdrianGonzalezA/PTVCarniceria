using System.Globalization;
using System.Text.RegularExpressions;

namespace Carnicerias.Domain.Inventory;

public sealed class BarcodeLayout
{
    private static readonly Regex FieldPattern = new(
        @"\A([a-z][a-z0-9_]*)\(([0-9]{1,3})\)\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IReadOnlyList<(string Name, int Width)> fields;

    private BarcodeLayout(IReadOnlyList<(string Name, int Width)> fields, int length)
    {
        this.fields = fields;
        Length = length;
    }

    public int Length { get; }

    public static BarcodeLayout Parse(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            throw new ArgumentException("A barcode formula is required.", nameof(formula));

        var tokens = formula.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 16)
            throw new ArgumentException("A barcode formula must have 1 to 16 fields.", nameof(formula));

        var fields = new List<(string Name, int Width)>(tokens.Length);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var length = 0;
        foreach (var token in tokens)
        {
            var match = FieldPattern.Match(token);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out var width) || width < 1 ||
                !names.Add(match.Groups[1].Value))
                throw new ArgumentException("The barcode formula has an invalid or repeated field.", nameof(formula));

            length += width;
            if (length > 80)
                throw new ArgumentException("A barcode cannot exceed 80 characters.", nameof(formula));
            fields.Add((match.Groups[1].Value.ToLowerInvariant(), width));
        }

        return new BarcodeLayout(fields, length);
    }

    public BarcodeRead Decode(string code)
    {
        if (code is null || code.Length != Length)
            throw new FormatException($"The barcode must have exactly {Length} characters.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        foreach (var (name, width) in fields)
        {
            values.Add(name, code.Substring(offset, width));
            offset += width;
        }

        return new BarcodeRead(values);
    }
}

public sealed class BarcodeRead
{
    private readonly IReadOnlyDictionary<string, string> values;

    internal BarcodeRead(IReadOnlyDictionary<string, string> values) => this.values = values;

    public IReadOnlyDictionary<string, string> Fields => values;

    public string Field(string name) => values.TryGetValue(name, out var value)
        ? value
        : throw new KeyNotFoundException($"Field '{name}' is not in this barcode layout.");

    public decimal ScaledDecimal(string name, int decimalPlaces)
    {
        var digits = Field(name);
        if (decimalPlaces < 0 || decimalPlaces > digits.Length)
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        if (digits.Any(character => character is < '0' or > '9'))
            throw new FormatException($"Field '{name}' must contain only digits.");

        if (!decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Field '{name}' exceeds the supported decimal range.");
        for (var index = 0; index < decimalPlaces; index++) value /= 10;
        return value;
    }
}
