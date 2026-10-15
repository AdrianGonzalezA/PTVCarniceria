using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carnicerias.Central.Api;

internal static class CentralJsonRequest
{
    private static readonly JsonSerializerOptions StrictWebOptions = new(JsonSerializerOptions.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<T?> ReadAsync<T>(HttpRequest request, int maximumBytes,
        CancellationToken cancellationToken, bool rejectUnknownFields = false)
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
        var options = rejectUnknownFields ? StrictWebOptions : JsonSerializerOptions.Web;
        try { return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), options); }
        catch (JsonException) { return default; }
    }
}
