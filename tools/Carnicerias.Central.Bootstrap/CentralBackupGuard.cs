using System.Security.Cryptography;

namespace Carnicerias.Central.Bootstrap;

public static class CentralBackupGuard
{
    private const long MinimumArchiveBytes = 1024;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    public static void Validate(string? path, string? expectedSha256, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetExtension(path), ".dump", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(expectedSha256)
            || expectedSha256.Length != 64)
        {
            throw InvalidBackup();
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.LinkTarget is not null
                || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || info.Length < MinimumArchiveBytes
                || now - info.LastWriteTimeUtc > MaximumAge
                || info.LastWriteTimeUtc - now > MaximumClockSkew)
            {
                throw InvalidBackup();
            }

            using var archive = info.OpenRead();
            Span<byte> header = stackalloc byte[5];
            if (archive.Read(header) != header.Length || !header.SequenceEqual("PGDMP"u8))
            {
                throw InvalidBackup();
            }

            archive.Position = 0;
            var actualHash = SHA256.HashData(archive);
            var expectedHash = Convert.FromHexString(expectedSha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                throw InvalidBackup();
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or FormatException)
        {
            throw InvalidBackup();
        }
    }

    private static InvalidOperationException InvalidBackup() =>
        new("Migration refused: provide a fresh verified PostgreSQL custom-format backup and its SHA-256 checksum.");
}
