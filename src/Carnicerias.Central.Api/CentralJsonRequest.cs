using System.Text.Json;

namespace Carnicerias.Central.Api;

internal static class CentralJsonRequest
{
    public static async Task<T?> ReadAsync<T>(HttpRequest request, int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType() || request.ContentLength > maximumBytes) return default;
        var bytes = new byte[maximumBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await request.Body.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count is 0 || count > maximumBytes) return default;
        try { return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count),
            JsonSerializerOptions.Web); }
        catch (JsonException) { return default; }
    }
}
