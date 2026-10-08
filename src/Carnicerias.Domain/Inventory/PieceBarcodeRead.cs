namespace Carnicerias.Domain.Inventory;

public sealed record PieceBarcodeRead(string ExternalIdentifier, decimal WeightKg)
{
    public static PieceBarcodeRead Parse(string formula, string identifierField,
        string weightField, int weightDecimals, string code)
    {
        if (string.IsNullOrWhiteSpace(identifierField) || string.IsNullOrWhiteSpace(weightField) ||
            string.Equals(identifierField.Trim(), weightField.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The piece identifier must be distinct from the weight field.",
                nameof(identifierField));

        var fields = BarcodeLayout.Parse(formula).Decode(code);
        var externalIdentifier = fields.Field(identifierField.Trim()).Trim();
        if (externalIdentifier.Length == 0)
            throw new FormatException("The barcode has an empty piece identifier.");
        var weightKg = fields.ScaledDecimal(weightField, weightDecimals);
        if (weightKg <= 0 || weightKg > 100000 || decimal.Round(weightKg, 3) != weightKg)
            throw new ArgumentOutOfRangeException(nameof(code), "The piece weight is invalid.");
        return new PieceBarcodeRead(externalIdentifier, weightKg);
    }
}
