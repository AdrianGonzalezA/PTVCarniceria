using System.Security.Cryptography;
using Carnicerias.Central.Bootstrap;

namespace Carnicerias.Central.Tests;

public sealed class CentralBackupGuardTests
{
    [Fact]
    public void AcceptsFreshPostgresArchiveWithMatchingChecksum()
    {
        var path = CreateArchive();
        try
        {
            CentralBackupGuard.Validate(path, Checksum(path), DateTimeOffset.UtcNow);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsWrongChecksum()
    {
        var path = CreateArchive();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                CentralBackupGuard.Validate(path, new string('0', 64), DateTimeOffset.UtcNow));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsStaleArchive()
    {
        var path = CreateArchive();
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            Assert.Throws<InvalidOperationException>(() =>
                CentralBackupGuard.Validate(path, Checksum(path), DateTimeOffset.UtcNow));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsFileWithoutPostgresArchiveHeader()
    {
        var path = CreateArchive();
        try
        {
            using (var stream = File.OpenWrite(path))
            {
                stream.Write("WRONG"u8);
            }

            Assert.Throws<InvalidOperationException>(() =>
                CentralBackupGuard.Validate(path, Checksum(path), DateTimeOffset.UtcNow));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"central-visual-{Guid.NewGuid():N}.dump");
        var bytes = new byte[2048];
        "PGDMP"u8.CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Checksum(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
