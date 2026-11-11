using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Fiscal;

/// <summary>Serializes fiscal issuance for one issuer, point and voucher type across API processes.</summary>
internal sealed class FiscalIssuanceLock : IAsyncDisposable
{
    private readonly PlatformAccessDbContext _db;
    private readonly long _key;

    private FiscalIssuanceLock(PlatformAccessDbContext db, long key)
    {
        _db = db;
        _key = key;
    }

    public static async Task<FiscalIssuanceLock?> TryAcquireAsync(PlatformAccessDbContext db,
        string issuerCuit, int pointOfSale, int voucherType, CancellationToken cancellationToken)
    {
        var key = Key(issuerCuit, pointOfSale, voucherType);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            command.Parameters.Add(new NpgsqlParameter("key", key));
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            {
                await db.Database.CloseConnectionAsync();
                return null;
            }
            return new FiscalIssuanceLock(db, key);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            command.Parameters.Add(new NpgsqlParameter("key", _key));
            if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
                throw new InvalidOperationException("The fiscal issuance lock was not held.");
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    internal static long Key(string issuerCuit, int pointOfSale, int voucherType)
    {
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(
            $"fiscal:{issuerCuit}:{pointOfSale}:{voucherType}"));
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }
}
