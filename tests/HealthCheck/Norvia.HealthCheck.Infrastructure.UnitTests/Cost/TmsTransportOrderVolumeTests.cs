using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Infrastructure.Cost;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Cost;

/// <summary>The health check asks the TMS API how many Transport Orders came in each day, as its own identity with the role the estate grants it.</summary>
public sealed class TmsTransportOrderVolumeTests : IDisposable
{
    private readonly Tms _tms = new();
    private readonly HttpClient _http;
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();

    public TmsTransportOrderVolumeTests()
    {
        _http = new HttpClient(_tms) { BaseAddress = new Uri("http://tms-api") };
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("tms-token", DateTimeOffset.MaxValue));
    }

    public void Dispose() => _http.Dispose();

    private TmsTransportOrderVolume Volume() => new(_http, _credential, Options.Create(new TmsApiOptions { BaseAddress = new Uri("http://tms-api"), Scope = "api://norvia-tms/.default" }));

    [Fact]
    public async Task CountPerDayAsync_ReadsTheVolumeOfTheWindowFromTheTmsApi_AsTheApp()
    {
        _tms.Answer = """[{"date":"2026-11-10","count":38},{"date":"2026-11-12","count":40}]""";

        var counts = await Volume().CountPerDayAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12), TestContext.Current.CancellationToken);

        counts.Should().BeEquivalentTo(new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 10)] = 38, [new DateOnly(2026, 11, 12)] = 40 });
        _tms.Request!.RequestUri!.PathAndQuery.Should().Be("/v1/transport-orders/volume?from=2026-11-10&to=2026-11-12");
        _tms.Request.Headers.Authorization!.ToString().Should().Be("Bearer tms-token");
        await _credential.Received().GetTokenAsync(Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "api://norvia-tms/.default"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountPerDayAsync_WhenTheTmsRefuses_Fails()
    {
        _tms.Status = HttpStatusCode.Forbidden;

        var read = () => Volume().CountPerDayAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class Tms : HttpMessageHandler
    {
        public string Answer { get; set; } = "[]";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") });
        }
    }
}
