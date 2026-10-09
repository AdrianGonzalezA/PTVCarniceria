namespace Carnicerias.Api.Fiscal;

/// <summary>In-process WSAA ticket cache for the selected company credential.</summary>
public sealed class ArcaHomologationTicketProvider(
    ArcaWsaaClient wsaa, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ArcaAccessTicket? _ticket;
    private string? _cacheKey;

    public async Task<ArcaAccessTicket> GetAsync(ArcaRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
            throw new InvalidOperationException("ARCA_HOMO_NOT_CONFIGURED");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var certificate = settings.LoadCertificate();
            var key = $"{settings.CompanyId:N}:{settings.IssuerCuit}:{certificate.Thumbprint}";
            if (_cacheKey == key && _ticket?.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(5))
                return _ticket;
            _ticket = await wsaa.RequestTicketAsync(certificate, settings.IssuerCuit, cancellationToken);
            _cacheKey = key;
            return _ticket;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
