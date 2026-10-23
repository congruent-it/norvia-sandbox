using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Infrastructure.Faults;
using Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Faults;

public sealed class FaultSwitchClientTests : IDisposable
{
    private const string Fault = """{"name":"driver-app-unavailable","owner":"driver-app","description":"Refuses every Trip.","on":true,"until":"2026-10-23T09:02:00+00:00","remainingSeconds":120}""";

    private readonly StubHandler _driverApp = new();
    private readonly IFaultTokens _tokens = Substitute.For<IFaultTokens>();
    private readonly FakeLogger<FaultSwitchClient> _logger = new();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly HttpClient _http;
    private readonly FaultSwitchClient _client;

    public FaultSwitchClientTests()
    {
        _http = new HttpClient(_driverApp);
        _tokens.GetTokenAsync(Arg.Any<FaultOwnerOptions>(), Arg.Any<CancellationToken>()).Returns("presenter-token");
        var options = Options.Create(new FaultsOptions
        {
            Owners = { ["driver-app"] = new FaultOwnerOptions { Address = new Uri("https://driver-app.example/"), Audience = "api://driver-app" } },
        });
        _client = new FaultSwitchClient(_http, _tokens, options, _activities, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _driverApp.Dispose();
    }

    [Fact]
    public async Task ListAsync_ReadsTheFaultsOfEveryOwner_AsThePresenter()
    {
        _driverApp.Answer(HttpStatusCode.OK, $"[{Fault}]");

        var faults = await _client.ListAsync(TestContext.Current.CancellationToken);

        var fault = faults.Should().ContainSingle().Subject;
        fault.Name.Should().Be("driver-app-unavailable");
        fault.Owner.Should().Be("driver-app");
        fault.On.Should().BeTrue();
        fault.Until.Should().Be(new DateTimeOffset(2026, 10, 23, 9, 2, 0, TimeSpan.Zero));
        _driverApp.Request!.Method.Should().Be(HttpMethod.Get);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver-app.example/v1/faults"));
        _driverApp.Request.Headers.Authorization!.ToString().Should().Be("Bearer presenter-token");
    }

    [Fact]
    public async Task ListAsync_WhenTheOwnerRefuses_Throws()
    {
        _driverApp.Answer(HttpStatusCode.Forbidden, string.Empty);

        var list = () => _client.ListAsync(TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SwitchAsync_WithADuration_PutsTheFaultOn_ForThatLong()
    {
        _driverApp.Answer(HttpStatusCode.OK, Fault);

        var view = await _client.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromSeconds(90.2), TestContext.Current.CancellationToken);

        view.On.Should().BeTrue();
        _driverApp.Request!.Method.Should().Be(HttpMethod.Put);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver-app.example/v1/faults/driver-app-unavailable"));
        _driverApp.Request.Headers.Authorization!.ToString().Should().Be("Bearer presenter-token");
        _driverApp.RequestBody.Should().Be("""{"on":true,"durationSeconds":91}""");
    }

    [Fact]
    public async Task SwitchAsync_WithoutADuration_PutsTheFaultOff()
    {
        _driverApp.Answer(HttpStatusCode.OK, Fault.Replace("\"on\":true", "\"on\":false", StringComparison.Ordinal));

        var view = await _client.SwitchAsync("driver-app", "driver-app-unavailable", null, TestContext.Current.CancellationToken);

        view.On.Should().BeFalse();
        _driverApp.RequestBody.Should().Be("""{"on":false,"durationSeconds":null}""");
    }

    [Fact]
    public async Task SwitchAsync_ForAnOwnerNobodyKnows_Throws()
    {
        var switchIt = () => _client.SwitchAsync("tms", "tms-slow", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await switchIt.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SwitchAsync_WhenTheOwnerRefuses_Throws()
    {
        _driverApp.Answer(HttpStatusCode.Forbidden, string.Empty);

        var switchIt = () => _client.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await switchIt.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SwitchAsync_SendsACorrelationId_AndLogsTheOutcomeUnderIt_NamingTheFault()
    {
        _driverApp.Answer(HttpStatusCode.OK, Fault);

        await _client.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var correlationId = _driverApp.Request!.Headers.GetValues("X-Correlation-ID").Should().ContainSingle().Subject;
        var log = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Level.Should().Be(LogLevel.Information);
        log.Scopes.Should().ContainSingle().Which.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>()
            .Which.Should().Contain([
                new("correlationId", correlationId), new("flow", "fault-switch"), new("fault", "driver-app-unavailable"), new("outcome", "Succeeded")]);
    }

    [Fact]
    public async Task SwitchAsync_WhenTheOwnerIsUnreachable_LogsItFailed_AndThrows()
    {
        _driverApp.Fail(new HttpRequestException("down"));

        var switchIt = () => _client.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await switchIt.Should().ThrowAsync<HttpRequestException>();
        _logger.Collector.GetSnapshot().Should().ContainSingle().Which.Scopes.Should().ContainSingle().Which
            .Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Which.Should().Contain([
                new("outcome", "Failed"), new("failureCode", "FaultOwnerUnreachable"), new("fault", "driver-app-unavailable")]);
    }
}
