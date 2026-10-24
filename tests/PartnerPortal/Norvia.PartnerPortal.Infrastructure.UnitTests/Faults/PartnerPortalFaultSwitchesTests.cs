using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Infrastructure.Faults;
using Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Faults;

public sealed class PartnerPortalFaultSwitchesTests : IDisposable
{
    private const string DriverAppFault = """{"name":"driver-app-unavailable","owner":"driver-app","description":"Refuses every Trip.","on":false,"until":null,"remainingSeconds":null}""";

    private readonly StubHandler _driverApp = new();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<PartnerPortalFaultSwitches> _logger = new();
    private readonly HttpClient _http;
    private readonly IFaults _own;
    private readonly PartnerPortalFaultSwitches _switches;

    public PartnerPortalFaultSwitchesTests()
    {
        _http = new HttpClient(_driverApp);
        var tokens = Substitute.For<IFaultTokens>();
        tokens.GetTokenAsync(Arg.Any<FaultOwnerOptions>(), Arg.Any<CancellationToken>()).Returns("presenter-token");
        var options = Options.Create(new FaultsOptions
        {
            Owners = { ["driver-app"] = new FaultOwnerOptions { Address = new Uri("https://driver-app.example/"), Audience = "api://driver-app" } },
        });
        var others = new FaultSwitchClient(_http, tokens, options, _activities, new FakeLogger<FaultSwitchClient>());

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddFaults(PartnerPortalFaultDefinitions.All);
        builder.Services.AddSingleton<TimeProvider>(_time);
        _own = builder.Services.BuildServiceProvider().GetRequiredService<IFaults>();
        _switches = new PartnerPortalFaultSwitches(others, _own, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _driverApp.Dispose();
    }

    [Fact]
    public async Task ListAsync_ReportsThePortalsFaultsBesideTheOthers_OrderedByOwnerThenName()
    {
        _driverApp.Answer(HttpStatusCode.OK, $"[{DriverAppFault}]");

        var faults = await _switches.ListAsync(TestContext.Current.CancellationToken);

        faults.Select(fault => (fault.Owner, fault.Name)).Should().Equal(
            ("driver-app", "driver-app-unavailable"),
            ("partner-portal", "carrier-answer-unmapped-reason"),
            ("partner-portal", "carrier-booking-endpoint-down"),
            ("partner-portal", "carrier-cancellation-endpoint-down"),
            ("partner-portal", "carrier-proof-heic-photo"),
            ("partner-portal", "carrier-tracking-unmapped-code"),
            ("partner-portal", "shipper-notification-endpoint-down"));
        faults.Should().OnlyContain(fault => fault.Description.Length > 0);
    }

    [Fact]
    public async Task ListAsync_EveryPortalFaultIsOffByDefault()
    {
        _driverApp.Answer(HttpStatusCode.OK, "[]");

        var faults = await _switches.ListAsync(TestContext.Current.CancellationToken);

        faults.Should().HaveCount(6).And.OnlyContain(fault => !fault.On && fault.Until == null);
    }

    [Fact]
    public async Task SwitchAsync_OnForADuration_SwitchesThePortalsFaultOn_UntilItExpires()
    {
        var view = await _switches.SwitchAsync("partner-portal", PartnerPortalFaultNames.TrackingUnmappedCode, TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        view.On.Should().BeTrue();
        view.Until.Should().Be(_time.GetUtcNow().AddMinutes(2));
        _own.IsOn(PartnerPortalFaultNames.TrackingUnmappedCode).Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(2));
        _own.IsOn(PartnerPortalFaultNames.TrackingUnmappedCode).Should().BeFalse();
    }

    [Fact]
    public async Task SwitchAsync_WithoutADuration_SwitchesThePortalsFaultOffAtOnce()
    {
        await _switches.SwitchAsync("partner-portal", PartnerPortalFaultNames.ProofHeicPhoto, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var view = await _switches.SwitchAsync("partner-portal", PartnerPortalFaultNames.ProofHeicPhoto, null, TestContext.Current.CancellationToken);

        view.On.Should().BeFalse();
        _own.IsOn(PartnerPortalFaultNames.ProofHeicPhoto).Should().BeFalse();
    }

    [Fact]
    public async Task SwitchAsync_ADurationLongerThanAllowed_Throws_AndLeavesTheFaultOff()
    {
        var switchOn = () => _switches.SwitchAsync("partner-portal", PartnerPortalFaultNames.BookingEndpointDown, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await switchOn.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _own.IsOn(PartnerPortalFaultNames.BookingEndpointDown).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task SwitchAsync_AnUnknownPortalFault_Throws(int? minutes)
    {
        var switchOn = () => _switches.SwitchAsync("partner-portal", "no-such-fault", minutes is { } on ? TimeSpan.FromMinutes(on) : null, TestContext.Current.CancellationToken);

        await switchOn.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SwitchAsync_APortalFault_IsTracedOnATrailOfItsOwn_WithTheFault()
    {
        await _switches.SwitchAsync("partner-portal", PartnerPortalFaultNames.CancellationEndpointDown, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var scope = _logger.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
        scope.Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "fault-switch",
            ["fault"] = "carrier-cancellation-endpoint-down",
            ["outcome"] = "Succeeded",
        });
        scope.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task SwitchAsync_AnotherOwnersFault_GoesToThatApplication()
    {
        _driverApp.Answer(HttpStatusCode.OK, DriverAppFault.Replace("\"on\":false", "\"on\":true", StringComparison.Ordinal));

        var view = await _switches.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        view.Owner.Should().Be("driver-app");
        _driverApp.Request!.Method.Should().Be(HttpMethod.Put);
    }
}
