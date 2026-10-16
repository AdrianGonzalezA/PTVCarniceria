using System.Net;
using System.Text.Json;

namespace Carnicerias.Central.Infrastructure;

public static class CentralApprovedEndpointConfiguration
{
    public static IReadOnlyList<CentralApprovedEndpoint> Parse(string? value, bool allowMissing = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowMissing) return [];
            throw InvalidConfiguration();
        }

        if (value.Length > 32_768) throw InvalidConfiguration();
        try
        {
            var entries = JsonSerializer.Deserialize<ApprovedEndpointInput[]>(
                value, JsonSerializerOptions.Web);
            if (entries is null or { Length: 0 or > 100 }) throw InvalidConfiguration();
            var result = new List<CentralApprovedEndpoint>(entries.Length);
            foreach (var entry in entries)
            {
                if (entry.Url is null || !Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0
                    || uri.Query.Length > 0 || uri.Fragment.Length > 0
                    || uri.AbsolutePath != "/" || entry.CentralCompanyId == Guid.Empty
                    || entry.Addresses is null
                    or { Length: 0 or > 16 }) throw InvalidConfiguration();

                var addresses = new List<IPAddress>(entry.Addresses.Length);
                foreach (var address in entry.Addresses)
                {
                    if (!IPAddress.TryParse(address, out var parsed)) throw InvalidConfiguration();
                    addresses.Add(parsed);
                }

                if (result.Any(existing => existing.Destination == uri
                    && existing.CentralCompanyId == entry.CentralCompanyId))
                    throw InvalidConfiguration();
                result.Add(new CentralApprovedEndpoint(uri, addresses, entry.CentralCompanyId));
            }

            return result;
        }
        catch (JsonException)
        {
            throw InvalidConfiguration();
        }
    }

    private static InvalidOperationException InvalidConfiguration() =>
        new("Central approved branch destinations are not configured correctly.");

    private sealed record ApprovedEndpointInput(string? Url, string[]? Addresses,
        Guid? CentralCompanyId);
}
