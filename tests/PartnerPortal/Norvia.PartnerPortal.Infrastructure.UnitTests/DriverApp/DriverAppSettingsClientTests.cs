using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.PartnerPortal.Infrastructure.DriverApp;
using Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.DriverApp;

public sealed class DriverAppSettingsClientTests : IDisposable
{
    private readonly StubHandler _driverApp = new();
    private readonly FakeLogger<DriverAppSettingsClient> _logger = new();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly HttpClient _http;
    private readonly DriverAppSettingsClient _client;

    public DriverAppSettingsClientTests()
    {
        _http = new HttpClient(_driverApp) { BaseAddress = new Uri("https://driver-app.example/") };
        _client = new DriverAppSettingsClient(_http, _activities, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _driverApp.Dispose();
    }

    [Theory]
    [InlineData("""{"unavailable":true}""", true)]
    [InlineData("""{"unavailable":false}""", false)]
    public async Task IsUnavailableAsync_ReadsTheDriverAppsSetting(string body, bool expected)
    {
        _driverApp.Answer(HttpStatusCode.OK, body);

        var unavailable = await _client.IsUnavailableAsync(TestContext.Current.CancellationToken);

        unavailable.Should().Be(expected);
        _driverApp.Request!.Method.Should().Be(HttpMethod.Get);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver-app.example/v1/settings/unavailable"));
    }

    [Fact]
    public async Task SetUnavailableAsync_PutsTheSetting()
    {
        _driverApp.Answer(HttpStatusCode.NoContent, string.Empty);

        await _client.SetUnavailableAsync(true, TestContext.Current.CancellationToken);

        _driverApp.Request!.Method.Should().Be(HttpMethod.Put);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver-app.example/v1/settings/unavailable"));
        _driverApp.RequestBody.Should().Be("""{"unavailable":true}""");
    }

    [Fact]
    public async Task SetUnavailableAsync_WhenTheDriverAppRefuses_Throws()
    {
        _driverApp.Answer(HttpStatusCode.InternalServerError, string.Empty);

        var set = () => _client.SetUnavailableAsync(true, TestContext.Current.CancellationToken);

        await set.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SetUnavailableAsync_SendsACorrelationId_AndLogsTheOutcomeUnderIt()
    {
        _driverApp.Answer(HttpStatusCode.NoContent, string.Empty);

        await _client.SetUnavailableAsync(true, TestContext.Current.CancellationToken);

        var correlationId = _driverApp.Request!.Headers.GetValues("X-Correlation-ID").Should().ContainSingle().Subject;
        var log = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Level.Should().Be(LogLevel.Information);
        log.Scopes.Should().ContainSingle().Which.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>()
            .Which.Should().Contain([
                new("correlationId", correlationId), new("flow", "driver-app-fault"), new("outcome", "Succeeded")]);
    }

    [Fact]
    public async Task SetUnavailableAsync_WhenTheDriverAppIsUnreachable_LogsItFailed_AndThrows()
    {
        _driverApp.Fail(new HttpRequestException("down"));

        var set = () => _client.SetUnavailableAsync(true, TestContext.Current.CancellationToken);

        await set.Should().ThrowAsync<HttpRequestException>();
        _logger.Collector.GetSnapshot().Should().ContainSingle().Which.Scopes.Should().ContainSingle().Which
            .Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Which.Should().Contain([
                new("outcome", "Failed"), new("failureCode", "DriverAppUnreachable")]);
    }
}
