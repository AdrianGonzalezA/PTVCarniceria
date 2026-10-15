using System.Globalization;

namespace Carnicerias.Central.Infrastructure;

public readonly record struct CentralBusinessWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    private static readonly string[] UtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    public static CentralBusinessWindow Today(DateTimeOffset nowUtc)
    {
        // .NET 10 resolves IANA IDs on Windows with ICU and on Linux from ICU/tzdata.
        // https://learn.microsoft.com/dotnet/api/system.timezoneinfo.findsystemtimezonebyid?view=net-10.0
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
        var localDate = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var midnight = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone));
        var to = nowUtc.ToUniversalTime();
        return new CentralBusinessWindow(from, to > from ? to : from.AddTicks(1));
    }

    public static bool TryCreate(string? fromUtc, string? toUtc, out CentralBusinessWindow window)
    {
        window = default;
        if (!TryReadUtc(fromUtc, out var from) || !TryReadUtc(toUtc, out var to)
            || from >= to || to - from > TimeSpan.FromDays(31)) return false;

        window = new CentralBusinessWindow(from, to);
        return true;
    }

    public static bool TryCreateLocalDates(string? fromDate, string? toDate,
        out CentralBusinessWindow window)
    {
        window = default;
        if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var first)
            || !DateOnly.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var last)
            || first > last || last == DateOnly.MaxValue) return false;

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            first.ToDateTime(TimeOnly.MinValue), zone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            last.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
        if (to - from > TimeSpan.FromDays(31)) return false;
        window = new CentralBusinessWindow(from, to);
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
