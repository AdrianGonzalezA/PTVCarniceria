using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralBranchIdentity(
    int ContractVersion, Guid InstallationId, Guid CompanyId, Guid BranchId);

public static class CentralInstallationProbe
{
    private const int MaximumResponseBytes = 4096;

    public static async Task<CentralBranchIdentity> VerifyAsync(
        CentralApprovedDestination approved,
        string? secret,
        Guid installationId,
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        using var client = CentralPinnedHttpClient.Create(approved, MaximumResponseBytes);
        return await VerifyWithClientAsync(client, approved, secret,
            installationId, companyId, branchId, cancellationToken);
    }

    internal static async Task<CentralBranchIdentity> VerifyWithClientAsync(
        HttpClient client,
        CentralApprovedDestination approved,
        string? secret,
        Guid installationId,
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (secret is null || secret.Length != 64 || !secret.All(Uri.IsHexDigit)
            || installationId == Guid.Empty || companyId == Guid.Empty || branchId == Guid.Empty)
        {
            throw Rejected();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(approved.Destination, "api/central/v1/installation"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        try
        {
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Rejected();
            }

            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var identity = JsonSerializer.Deserialize<CentralBranchIdentity>(
                bytes, JsonSerializerOptions.Web);
            if (identity is null || identity.ContractVersion != 1
                || identity.InstallationId != installationId
                || identity.CompanyId != companyId
                || identity.BranchId != branchId)
            {
                throw Rejected();
            }

            return identity;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or JsonException or TaskCanceledException)
        {
            throw Rejected();
        }
    }

    private static InvalidOperationException Rejected() =>
        new("Branch identity could not be verified; registration was refused.");
}
