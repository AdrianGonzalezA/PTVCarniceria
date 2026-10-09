using System.Text;

namespace Carnicerias.Infrastructure;

public enum SaleDocumentType { NonFiscalTicket, FiscalTicket, ElectronicInvoice }

public enum SaleRecipientTaxStatus { FinalConsumer, Registered, SmallTaxpayer, Exempt }

// This records what was requested at checkout; it is not proof that a fiscal document was issued.
public sealed record SaleDocumentChoice(
    SaleDocumentType Type, SaleRecipientTaxStatus RecipientTaxStatus,
    string? RecipientName, string? RecipientDocumentNumber, string? RecipientAddress)
{
    // ARCA's published identification threshold for final consumers, checked again before issuance.
    public const decimal FinalConsumerIdentificationThreshold = 10_000_000m;

    public static SaleDocumentChoice Create(string? type, string? taxStatus, string? name,
        string? documentNumber, string? address, decimal saleTotal)
    {
        var documentType = type switch
        {
            "nonFiscalTicket" => SaleDocumentType.NonFiscalTicket,
            "fiscalTicket" => SaleDocumentType.FiscalTicket,
            "electronicInvoice" => SaleDocumentType.ElectronicInvoice,
            _ => throw new ArgumentException("Unknown requested document type.", nameof(type))
        };
        var recipientTaxStatus = taxStatus switch
        {
            "finalConsumer" => SaleRecipientTaxStatus.FinalConsumer,
            "registered" => SaleRecipientTaxStatus.Registered,
            "smallTaxpayer" => SaleRecipientTaxStatus.SmallTaxpayer,
            "exempt" => SaleRecipientTaxStatus.Exempt,
            _ => throw new ArgumentException("Unknown recipient tax status.", nameof(taxStatus))
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(saleTotal);
        if (documentType == SaleDocumentType.NonFiscalTicket)
            return new(documentType, recipientTaxStatus, null, null, null);

        var normalizedName = Normalize(name);
        var normalizedAddress = Normalize(address);
        var digits = documentNumber is null ? null : new string(documentNumber.Where(char.IsAsciiDigit).ToArray());
        if (documentNumber is not null && documentNumber.Any(character =>
                !char.IsAsciiDigit(character) && character is not '-' and not ' '))
            throw new ArgumentException("Invalid recipient document number.", nameof(documentNumber));
        if (normalizedName?.Length > 200 || normalizedAddress?.Length > 200)
            throw new ArgumentException("Recipient details are too long.");

        if (recipientTaxStatus != SaleRecipientTaxStatus.FinalConsumer)
        {
            if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(normalizedAddress) ||
                digits is null || !ValidCuit(digits))
                throw new ArgumentException("Identified fiscal recipients need name, valid CUIT and address.");
        }
        else if (digits is { Length: > 0 } && !ValidFinalConsumerDocument(digits))
            throw new ArgumentException("Invalid final consumer document number.", nameof(documentNumber));
        else if (saleTotal >= FinalConsumerIdentificationThreshold && string.IsNullOrEmpty(digits))
            throw new ArgumentException("Final consumer identification is required for this amount.");

        return new(documentType, recipientTaxStatus, normalizedName,
            string.IsNullOrEmpty(digits) ? null : digits, normalizedAddress);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize(NormalizationForm.FormKC);

    private static bool ValidFinalConsumerDocument(string value) =>
        value.Length is 7 or 8 && value.All(char.IsAsciiDigit) || ValidCuit(value);

    private static bool ValidCuit(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit)) return false;
        ReadOnlySpan<int> weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var sum = 0;
        for (var index = 0; index < weights.Length; index++)
            sum += (value[index] - '0') * weights[index];
        var check = 11 - sum % 11;
        return (check == 11 ? 0 : check == 10 ? 9 : check) == value[10] - '0';
    }
}
