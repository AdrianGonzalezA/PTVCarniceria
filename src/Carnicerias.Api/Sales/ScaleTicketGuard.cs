using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Carnicerias.Api.Catalog;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Sales;

/// <summary>
/// A consolidated scale QR is one ticket. Its source header is reserved by an open
/// draft and permanently used by a confirmed sale. The lock serializes checks across boxes.
/// </summary>
public static class ScaleTicketGuard
{
    public static bool TryGetCompleteTicketKeys(IEnumerable<string?> references,
        out string[] ticketKeys)
    {
        ticketKeys = [];
        var qrReferences = references.Where(reference => reference?.StartsWith("QR:",
            StringComparison.Ordinal) == true).Select(reference => reference!).ToArray();
        if (qrReferences.Length == 0) return true;
        var groups = new Dictionary<string, List<(string Header, int LineNumber)>>();
        foreach (var reference in qrReferences)
        {
            if (!KretzScaleLabel.TryDecode(reference, out _)) return false;
            var header = reference.Substring(3, 23);
            var key = header[..^2];
            var lineNumber = int.Parse(reference.AsSpan(27, 2),
                System.Globalization.CultureInfo.InvariantCulture);
            if (!groups.TryGetValue(key, out var lines))
                groups.Add(key, lines = []);
            lines.Add((header, lineNumber));
        }
        foreach (var group in groups)
        {
            var lines = group.Value;
            var header = lines[0].Header;
            var expected = int.Parse(header.AsSpan(21, 2),
                System.Globalization.CultureInfo.InvariantCulture);
            if (lines.Count != expected || lines.Any(line => line.Header != header) ||
                lines.Select(line => line.LineNumber).Distinct().Count() != expected)
                return false;
        }
        ticketKeys = groups.Keys.Order(StringComparer.Ordinal).ToArray();
        return true;
    }

    public static async Task<string?> CheckAndLockAsync(PlatformAccessDbContext db,
        Guid companyId, Guid branchId, Guid draftId, IReadOnlyList<string> ticketKeys,
        CancellationToken cancellationToken)
    {
        foreach (var key in ticketKeys)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"scale-ticket-v1:{companyId:N}:{branchId:N}:{key}"));
            var lockKey = BinaryPrimitives.ReadInt64BigEndian(bytes);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
            var prefix = $"QR:{key}";
            var charged = await (from line in db.ConfirmedSaleLines.AsNoTracking()
                join sale in db.ConfirmedSales.AsNoTracking() on line.SaleId equals sale.Id
                where line.CompanyId == companyId && sale.BranchId == branchId &&
                    line.ScaleLabelReference != null &&
                    line.ScaleLabelReference.StartsWith(prefix)
                select line.Id).AnyAsync(cancellationToken);
            if (charged) return "SCALE_TICKET_ALREADY_CHARGED";
            var reserved = await (from line in db.Set<SaleDraftLine>().AsNoTracking()
                join draft in db.SaleDrafts.AsNoTracking() on line.SaleDraftId equals draft.Id
                where line.CompanyId == companyId && draft.BranchId == branchId &&
                    draft.Id != draftId && draft.Status == SaleDraftStatus.Draft &&
                    line.ScaleLabelReference != null &&
                    line.ScaleLabelReference.StartsWith(prefix)
                select line.Id).AnyAsync(cancellationToken);
            if (reserved) return "SCALE_TICKET_RESERVED";
        }
        return null;
    }
}
