using System.Net;
using System.Globalization;
using System.Text.Json;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchSummaryProbeTests
{
    private readonly Guid centralCompanyId = Guid.NewGuid();
    private readonly Guid installationId = Guid.NewGuid();
    private readonly Guid remoteCompanyId = Guid.NewGuid();
    private readonly Guid branchId = Guid.NewGuid();
    private static readonly CentralBusinessWindow Window = new(
        new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AcceptsOnlyMatchingSummaryAndUsesBearerOnExpectedPath()
    {
        var handler = new SummaryHandler();
        handler.Response = SummaryResponse();
        using var client = new HttpClient(handler);

        var summary = await CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(),
            new string('A', 64), Installation(), Window, CancellationToken.None);

        Assert.Equal(2, summary.SaleCount);
        Assert.Equal(150m, summary.SalesTotal);
        Assert.Equal(-5m, summary.OpenShiftCashBalance);
        Assert.Equal("Bearer " + new string('A', 64), handler.Authorization);
        Assert.Equal("/api/central/v1/summary", handler.Path);
        Assert.Equal(Window.FromUtc, handler.FromUtc);
        Assert.Equal(Window.ToUtc, handler.ToUtc);
    }

    [Fact]
    public async Task RefusesWrongIdentityCurrencyWindowOrInconsistentAmounts()
    {
        var handler = new SummaryHandler();
        using var client = new HttpClient(handler);
        foreach (var malformed in new object[]
        {
            new { installationId = Guid.NewGuid() },
            new { companyId = Guid.NewGuid() },
            new { branchId = Guid.NewGuid() },
            new { fromUtc = Window.FromUtc.AddDays(-1) },
            new { currency = "USD" },
            new { saleCount = -1 },
            new { salesTotal = -1m },
            new { salePaymentsTotal = 999m },
            new { openShiftCount = -1 },
            new { paymentsByMethod = new[] { new { method = "cash", amount = -2m } } }
        })
        {
            handler.Response = SummaryResponse(malformed);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(),
                    new string('A', 64), Installation(), Window, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RefusesMissingTokenRedirectAndMissingFields()
    {
        var handler = new SummaryHandler { Response = SummaryResponse() };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(), null,
                Installation(), Window, CancellationToken.None));
        Assert.Null(handler.Path);

        handler.Response = new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://elsewhere.example/") }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(),
                new string('A', 64), Installation(), Window, CancellationToken.None));

        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 17_000))
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(),
                new string('A', 64), Installation(), Window, CancellationToken.None));

        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"contractVersion\":1}")
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralBranchSummaryProbe.ReadWithClientAsync(client, Approved(),
                new string('A', 64), Installation(), Window, CancellationToken.None));
    }

    private CentralInstallation Installation() => CentralInstallation.Create(
        installationId, centralCompanyId, remoteCompanyId, branchId,
        "Centro", "https://branch.example/", "BRANCH_TOKEN");

    private static CentralApprovedDestination Approved() =>
        new(new Uri("https://branch.example/"), IPAddress.Parse("10.10.1.5"));

    private HttpResponseMessage SummaryResponse(object? overrides = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["contractVersion"] = 1,
            ["installationId"] = installationId,
            ["companyId"] = remoteCompanyId,
            ["branchId"] = branchId,
            ["fromUtc"] = Window.FromUtc,
            ["toUtc"] = Window.ToUtc,
            ["generatedAtUtc"] = Window.ToUtc,
            ["currency"] = "ARS",
            ["saleCount"] = 2,
            ["salesTotal"] = 150m,
            ["salePaymentsTotal"] = 120m,
            ["paymentsByMethod"] = new[] { new { method = "cash", amount = 120m } },
            ["openShiftCount"] = 1,
            ["openShiftCashBalance"] = -5m
        };
        if (overrides is not null)
        {
            foreach (var item in JsonSerializer.SerializeToElement(overrides).EnumerateObject())
                fields[item.Name] = item.Value;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(fields))
        };
    }

    private sealed class SummaryHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.NotFound);
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        public DateTimeOffset? FromUtc { get; private set; }
        public DateTimeOffset? ToUtc { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Path = request.RequestUri?.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? "");
            FromUtc = DateTimeOffset.Parse(query["fromUtc"]!, CultureInfo.InvariantCulture);
            ToUtc = DateTimeOffset.Parse(query["toUtc"]!, CultureInfo.InvariantCulture);
            return Task.FromResult(Response);
        }
    }
}
