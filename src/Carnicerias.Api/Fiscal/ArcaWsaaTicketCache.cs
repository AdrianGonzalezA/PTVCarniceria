using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Carnicerias.Api.Fiscal;

/// <summary>
/// Encrypted WSAA ticket cache shared by API processes running on the same server.
/// The file lock covers both the cache check and a new WSAA request.
/// </summary>
public sealed class ArcaWsaaTicketCache(
    IDataProtectionProvider protectionProvider, IConfiguration configuration,
    ILogger<ArcaWsaaTicketCache> logger)
{
    private static readonly Action<ILogger, Exception?> LogCacheWriteFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "WsaaTicketCacheWriteFailure"),
            "WSAA ticket was issued but could not be persisted for reuse");
    private static readonly Action<ILogger, Exception?> LogCacheReadFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "WsaaTicketCacheReadFailure"),
            "WSAA ticket cache could not be read; requesting a new ticket");
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Arca.Wsaa.Tickets.v1");
    private readonly string _directory = ResolveDirectory(configuration);

    public async Task<ArcaAccessTicket> GetOrRequestAsync(string key, string issuerCuit,
        TimeProvider timeProvider, Func<CancellationToken, Task<ArcaAccessTicket>> request,
        CancellationToken cancellationToken)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{name}.ticket");
        await using var fileLock = await AcquireLockAsync(path + ".lock", cancellationToken);
        var now = timeProvider.GetUtcNow();
        var cached = await ReadAsync(path, issuerCuit, now, cancellationToken);
        if (cached is not null)
            return cached;

        var ticket = await request(cancellationToken);
        try
        {
            var payload = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(ticket));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, payload, cancellationToken);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LogCacheWriteFailure(logger, error);
        }
        return ticket;
    }

    private async Task<ArcaAccessTicket?> ReadAsync(string path, string issuerCuit,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
            if (encrypted.Length is 0 or > 100_000)
                throw new InvalidDataException("Unexpected WSAA ticket cache size.");
            var ticket = JsonSerializer.Deserialize<ArcaAccessTicket>(_protector.Unprotect(encrypted));
            if (ticket?.Cuit != issuerCuit || ticket.ExpiresAtUtc is not { } expiry ||
                expiry <= now.AddMinutes(5) || string.IsNullOrWhiteSpace(ticket.Token) ||
                string.IsNullOrWhiteSpace(ticket.Sign))
                return null;
            return ticket;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or InvalidDataException)
        {
            LogCacheReadFailure(logger, error);
            return null;
        }
    }

    private static async Task<FileStream> AcquireLockAsync(string path,
        CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < until)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private static string ResolveDirectory(IConfiguration configuration)
    {
        var configured = configuration["ARCA_WSAA_CACHE_DIRECTORY"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new InvalidOperationException("Configure ARCA_WSAA_CACHE_DIRECTORY for this server.");
        return Path.Combine(localData, "Carnicerias", "ArcaWsaaTickets");
    }
}
