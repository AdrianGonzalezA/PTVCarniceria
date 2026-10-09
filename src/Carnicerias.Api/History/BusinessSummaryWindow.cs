namespace Carnicerias.Api.History;

public readonly record struct BusinessSummaryWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    private static readonly TimeZoneInfo ArgentinaZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public static BusinessSummaryWindow TodayArgentina(TimeProvider timeProvider)
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ArgentinaZone);
        return ForArgentinaDate(DateOnly.FromDateTime(localNow.Date));
    }

    public static BusinessSummaryWindow ForArgentinaDate(DateOnly date)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), ArgentinaZone);
        var to = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), ArgentinaZone);
        return new BusinessSummaryWindow(new DateTimeOffset(from), new DateTimeOffset(to));
    }

    public static bool TryCreate(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        out BusinessSummaryWindow window)
    {
        window = default;
        if (fromUtc is null || toUtc is null || fromUtc.Value.Offset != TimeSpan.Zero ||
            toUtc.Value.Offset != TimeSpan.Zero || fromUtc >= toUtc ||
            toUtc - fromUtc > TimeSpan.FromDays(366)) return false;
        window = new BusinessSummaryWindow(fromUtc.Value, toUtc.Value);
        return true;
    }
}
