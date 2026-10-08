using System.Text;
using Carnicerias.Domain.Inventory;

namespace Carnicerias.Infrastructure;

public sealed class BarcodeProfile
{
    private BarcodeProfile()
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
        Formula = string.Empty;
        WeightField = string.Empty;
    }

    public BarcodeProfile(Guid companyId, string name, int revision, string formula,
        string weightField, int weightDecimals, DateTime createdAtUtc)
    {
        if (companyId == Guid.Empty || revision < 1 || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A company, revision and UTC creation time are required.");
        var normalized = name?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 120 ||
            string.IsNullOrWhiteSpace(formula) || formula.Length > 512 ||
            string.IsNullOrWhiteSpace(weightField) || weightField.Length > 80)
            throw new ArgumentException("The barcode profile has invalid text.");

        var layout = BarcodeLayout.Parse(formula);
        var field = weightField.Trim();
        if (weightDecimals < 0 || weightDecimals > 6 ||
            layout.FieldWidth(field) == 0 || weightDecimals > layout.FieldWidth(field))
            throw new ArgumentException("The barcode weight definition is invalid.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        Name = normalized;
        NormalizedName = normalized.ToUpperInvariant();
        Revision = revision;
        Formula = formula.Trim();
        WeightField = field.ToLowerInvariant();
        WeightDecimals = weightDecimals;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public int Revision { get; private set; }
    public string Formula { get; private set; }
    public string WeightField { get; private set; }
    public int WeightDecimals { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
