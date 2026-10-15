using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralPaymentSummary(string Method, decimal Amount);
public sealed record CentralBranchSummary(DateTimeOffset GeneratedAtUtc, int SaleCount,
    decimal SalesTotal, decimal SalePaymentsTotal, IReadOnlyList<CentralPaymentSummary> PaymentsByMethod,
    int OpenShiftCount, decimal OpenShiftCashBalance);

public static class CentralBranchSummaryProbe
{
    private const int MaximumResponseBytes = 16_384;
    private static readonly HashSet<string> PaymentMethods =
        ["cash", "debit", "credit", "transfer", "mercadoPago", "cheque", "unknown"];

    public static async Task<CentralBranchSummary> ReadAsync(
        CentralApprovedDestination approved, string? secret,
        CentralInstallation installation, CentralBusinessWindow window,
        CancellationToken cancellationToken)
    {
        using var client = CentralPinnedHttpClient.Create(approved, MaximumResponseBytes);
        return await ReadWithClientAsync(client, approved, secret, installation, window,
            cancellationToken);
    }

    internal static async Task<CentralBranchSummary> ReadWithClientAsync(
        HttpClient client, CentralApprovedDestination approved, string? secret,
        CentralInstallation installation, CentralBusinessWindow window,
        CancellationToken cancellationToken)
    {
        if (secret is null || secret.Length != 64 || !secret.All(Uri.IsHexDigit))
            throw Rejected();

        var from = Uri.EscapeDataString(window.FromUtc.ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(window.ToUtc.ToString("O", CultureInfo.InvariantCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(approved.Destination, $"api/central/v1/summary?fromUtc={from}&toUtc={to}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        try
        {
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK) throw Rejected();

            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var wire = JsonSerializer.Deserialize<SummaryWire>(bytes, JsonSerializerOptions.Web);
            if (wire is null || wire.ContractVersion != 1
                || wire.InstallationId != installation.InstallationId
                || wire.CompanyId != installation.RemoteCompanyId
                || wire.BranchId != installation.RemoteBranchId
                || wire.FromUtc != window.FromUtc || wire.ToUtc != window.ToUtc
                || wire.FromUtc?.Offset != TimeSpan.Zero || wire.ToUtc?.Offset != TimeSpan.Zero
                || wire.GeneratedAtUtc is null || wire.GeneratedAtUtc.Value == default
                || wire.GeneratedAtUtc.Value.Offset != TimeSpan.Zero
                || wire.Currency != "ARS" || wire.SaleCount is null or < 0
                || wire.SalesTotal is null or < 0 || wire.SalePaymentsTotal is null or < 0
                || wire.OpenShiftCount is null or < 0 || wire.OpenShiftCashBalance is null
                || wire.PaymentsByMethod is not { Length: <= 16 } payments
                || payments.Any(payment => payment is null || payment.Method is null
                    || !PaymentMethods.Contains(payment.Method)
                    || payment.Amount is null or < 0)
                || payments.Select(payment => payment!.Method).Distinct().Count() != payments.Length
                || payments.Sum(payment => payment!.Amount!.Value) != wire.SalePaymentsTotal)
            {
                throw Rejected();
            }

            return new CentralBranchSummary(wire.GeneratedAtUtc.Value, wire.SaleCount.Value,
                wire.SalesTotal.Value, wire.SalePaymentsTotal.Value,
                payments.Select(payment => new CentralPaymentSummary(
                    payment!.Method!, payment.Amount!.Value)).ToArray(),
                wire.OpenShiftCount.Value, wire.OpenShiftCashBalance.Value);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or JsonException or TaskCanceledException
                                          or OverflowException)
        {
            throw Rejected();
        }
    }

    private static InvalidOperationException Rejected() =>
        new("Branch summary could not be verified.");

    private sealed record SummaryWire(int? ContractVersion, Guid? InstallationId,
        Guid? CompanyId, Guid? BranchId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc,
        DateTimeOffset? GeneratedAtUtc, string? Currency, int? SaleCount, decimal? SalesTotal,
        decimal? SalePaymentsTotal, PaymentWire[]? PaymentsByMethod,
        int? OpenShiftCount, decimal? OpenShiftCashBalance);

    private sealed record PaymentWire(string? Method, decimal? Amount);
}
