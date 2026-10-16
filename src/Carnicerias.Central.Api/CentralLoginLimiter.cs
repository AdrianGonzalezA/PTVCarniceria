using Microsoft.Extensions.Caching.Memory;

namespace Carnicerias.Central.Api;

public sealed class CentralLoginLimiter : IDisposable
{
    private const int AttemptsPerWindow = 10;
    private readonly object gate = new();
    private readonly MemoryCache attempts = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public bool TryAcquire(string companyCode, string credential)
    {
        var key = $"{companyCode.Trim().ToUpperInvariant()}:{credential.Trim().ToUpperInvariant()}";
        lock (gate)
        {
            var window = attempts.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                entry.Size = 1;
                return new AttemptWindow();
            })!;
            if (window.Count >= AttemptsPerWindow)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    public void Dispose() => attempts.Dispose();

    private sealed class AttemptWindow
    {
        public int Count { get; set; }
    }
}
