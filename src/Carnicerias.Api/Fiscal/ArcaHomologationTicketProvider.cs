namespace Carnicerias.Api.Fiscal;

/// <summary>In-process WSAA ticket cache for the selected company credential.</summary>
public sealed class ArcaHomologationTicketProvider(
    ArcaWsaaClient wsaa, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ArcaAccessTicket> _tickets = [];

    public Task<ArcaAccessTicket> GetAsync(ArcaRuntimeSettings settings,
        CancellationToken cancellationToken)
        => GetAsync(settings, ArcaWsaaClient.InvoiceService, cancellationToken);

    public async Task<ArcaAccessTicket> GetAsync(ArcaRuntimeSettings settings,
        string service, CancellationToken cancellationToken)
    {
        if (service is not (ArcaWsaaClient.InvoiceService or ArcaWsaaClient.RegistryService))
            throw new ArgumentException("Unsupported WSAA service.", nameof(service));
        if (!settings.IsConfigured)
            throw new InvalidOperationException("ARCA_HOMO_NOT_CONFIGURED");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var certificate = settings.LoadCertificate();
            var key = $"{settings.CompanyId:N}:{settings.IssuerCuit}:{certificate.Thumbprint}:{service}";
            var now = timeProvider.GetUtcNow();
            if (_tickets.TryGetValue(key, out var cached) &&
                cached.ExpiresAtUtc > now.AddMinutes(5))
                return cached;
            foreach (var stale in _tickets.Where(item => item.Value.ExpiresAtUtc <= now)
                         .Select(item => item.Key).ToArray())
                _tickets.Remove(stale);
            var ticket = await wsaa.RequestTicketAsync(certificate, settings.IssuerCuit,
                service, cancellationToken);
            _tickets[key] = ticket;
            return ticket;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
