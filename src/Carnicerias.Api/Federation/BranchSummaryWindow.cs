using System.Globalization;
using Carnicerias.Api.History;

namespace Carnicerias.Api.Federation;

public readonly record struct BranchSummaryWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    private static readonly string[] UtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    public static bool TryCreate(string? fromUtc, string? toUtc, out BranchSummaryWindow window)
    {
        window = default;
        if (!TryReadUtc(fromUtc, out var from) || !TryReadUtc(toUtc, out var to) ||
            !BusinessSummaryWindow.TryCreate(from, to, out _) ||
            to - from > TimeSpan.FromDays(31)) return false;

        window = new BranchSummaryWindow(from, to);
        return true;
    }

    private static bool TryReadUtc(string? value, out DateTimeOffset result)
    {
        result = default;
        return value is not null &&
            (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal)) &&
            DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out result) &&
            result.Offset == TimeSpan.Zero;
    }
}
