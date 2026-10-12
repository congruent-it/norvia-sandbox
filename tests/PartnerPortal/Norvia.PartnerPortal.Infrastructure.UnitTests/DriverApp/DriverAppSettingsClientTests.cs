using System.Net;
using Norvia.PartnerPortal.Infrastructure.DriverApp;
using Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.DriverApp;

public sealed class DriverAppSettingsClientTests : IDisposable
{
    private readonly StubHandler _driverApp = new();
    private readonly HttpClient _http;
    private readonly DriverAppSettingsClient _client;

    public DriverAppSettingsClientTests()
    {
        _http = new HttpClient(_driverApp) { BaseAddress = new Uri("https://driver-app.example/") };
        _client = new DriverAppSettingsClient(_http);
    }

    public void Dispose()
    {
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
}
