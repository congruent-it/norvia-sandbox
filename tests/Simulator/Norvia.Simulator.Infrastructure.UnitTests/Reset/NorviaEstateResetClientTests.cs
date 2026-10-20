using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Infrastructure.DriverApi;
using Norvia.Simulator.Infrastructure.ErpApi;
using Norvia.Simulator.Infrastructure.Reset;
using Norvia.Simulator.Infrastructure.TmsApi;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.Reset;

public sealed class NorviaEstateResetClientTests : IDisposable
{
    private readonly StubHandler _estate = new();
    private readonly ITmsTokens _tmsTokens = Substitute.For<ITmsTokens>();
    private readonly IDriverTokens _driverTokens = Substitute.For<IDriverTokens>();
    private readonly IErpTokens _erpTokens = Substitute.For<IErpTokens>();
    private readonly HttpClient _http;

    public NorviaEstateResetClientTests()
    {
        _http = new HttpClient(_estate);
        _tmsTokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tms-token");
        _driverTokens.GetResetTokenAsync(Arg.Any<CancellationToken>()).Returns("driver-token");
        _erpTokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("erp-token");
    }

    public void Dispose()
    {
        _http.Dispose();
        _estate.Dispose();
    }

    private NorviaEstateResetClient Client(PartnerPortalResetOptions? portal = null, ErpApiOptions? erp = null) => new(
        _http,
        _tmsTokens,
        _driverTokens,
        _erpTokens,
        Options.Create(new TmsApiOptions { BaseAddress = new Uri("https://tms.example/") }),
        Options.Create(new DriverApiOptions { BaseAddress = new Uri("https://driver.example") }),
        Options.Create(erp ?? new ErpApiOptions { BaseAddress = new Uri("https://erp.example") }),
        Options.Create(portal ?? new PartnerPortalResetOptions { BaseAddress = new Uri("https://portal.example"), ResetApiKey = "portal-key" }),
        new FakeLogger<NorviaEstateResetClient>());

    [Fact]
    public async Task Check_Tms_AsksTheResetDoorWithTheSimulatorsToken()
    {
        _estate.Answer(HttpStatusCode.OK, """{"enabled":true,"busySubscription":null}""");

        var readiness = await Client().CheckAsync(EstateApp.Tms, CancellationToken.None);

        _estate.Request!.Method.Should().Be(HttpMethod.Get);
        _estate.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/reset"));
        _estate.Request.Headers.Authorization!.Parameter.Should().Be("tms-token");
        readiness.Should().Be(new AppReadiness(EstateApp.Tms, Reachable: true, Enabled: true, BusySubscription: null));
    }

    [Fact]
    public async Task Check_DriverApp_UsesTheResetToken()
    {
        _estate.Answer(HttpStatusCode.OK, """{"enabled":true,"busySubscription":"driver-app-events/driver-reports"}""");

        var readiness = await Client().CheckAsync(EstateApp.DriverApp, CancellationToken.None);

        _estate.Request!.RequestUri.Should().Be(new Uri("https://driver.example/v1/reset"));
        _estate.Request.Headers.Authorization!.Parameter.Should().Be("driver-token");
        readiness.BusySubscription.Should().Be("driver-app-events/driver-reports");
    }

    [Fact]
    public async Task Check_Erp_AsksTheResetDoorWithTheSimulatorsToken()
    {
        _estate.Answer(HttpStatusCode.OK, """{"enabled":true,"busySubscription":"outbox (unpublished events)"}""");

        var readiness = await Client().CheckAsync(EstateApp.Erp, CancellationToken.None);

        _estate.Request!.Method.Should().Be(HttpMethod.Get);
        _estate.Request.RequestUri.Should().Be(new Uri("https://erp.example/v1/reset"));
        _estate.Request.Headers.Authorization!.Parameter.Should().Be("erp-token");
        readiness.Should().Be(new AppReadiness(EstateApp.Erp, Reachable: true, Enabled: true, "outbox (unpublished events)"));
    }

    [Fact]
    public async Task Reset_ErpWithoutAnAddress_IsRefusedWithoutCallingIt()
    {
        var result = await Client(erp: new ErpApiOptions()).ResetAsync(EstateApp.Erp, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Refusal.Should().Be("The Simulator isn't set up to reach the ERP's reset.");
        _estate.Request.Should().BeNull();
    }

    [Fact]
    public async Task Check_PartnerPortal_SendsItsKeyNotAToken()
    {
        _estate.Answer(HttpStatusCode.OK, """{"enabled":false,"busySubscription":null}""");

        var readiness = await Client().CheckAsync(EstateApp.PartnerPortal, CancellationToken.None);

        _estate.Request!.RequestUri.Should().Be(new Uri("https://portal.example/reset/v1"));
        _estate.Request.Headers.GetValues("X-Api-Key").Should().Equal("portal-key");
        _estate.Request.Headers.Authorization.Should().BeNull();
        readiness.Should().Be(new AppReadiness(EstateApp.PartnerPortal, Reachable: true, Enabled: false, BusySubscription: null));
    }

    [Fact]
    public async Task Check_PartnerPortalNotConfigured_IsNotReachable()
    {
        var readiness = await Client(new PartnerPortalResetOptions()).CheckAsync(EstateApp.PartnerPortal, CancellationToken.None);

        readiness.Reachable.Should().BeFalse();
        _estate.Request.Should().BeNull();
    }

    [Fact]
    public async Task Check_AnApplicationThatRefusesTheCall_IsNotReachable()
    {
        _estate.Answer(HttpStatusCode.Forbidden, "{}");

        (await Client().CheckAsync(EstateApp.Tms, CancellationToken.None)).Reachable.Should().BeFalse();
    }

    [Fact]
    public async Task Check_AnApplicationThatDoesNotAnswer_IsNotReachable()
    {
        _estate.Fail(new HttpRequestException("down"));

        (await Client().CheckAsync(EstateApp.Tms, CancellationToken.None)).Reachable.Should().BeFalse();
    }

    [Fact]
    public async Task Check_AnEmptyBody_IsNotReachable()
    {
        _estate.Answer(HttpStatusCode.OK, "null");

        (await Client().CheckAsync(EstateApp.Tms, CancellationToken.None)).Reachable.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_Tms_PostsAndReportsWhatWasDeleted()
    {
        _estate.Answer(HttpStatusCode.OK, """{"deleted":[{"kind":"Transport Orders","count":12},{"kind":"Shipments","count":12}]}""");

        var result = await Client().ResetAsync(EstateApp.Tms, CancellationToken.None);

        _estate.Request!.Method.Should().Be(HttpMethod.Post);
        _estate.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/reset"));
        result.Succeeded.Should().BeTrue();
        result.Deleted.Should().Equal(new DeletedKind("Transport Orders", 12), new DeletedKind("Shipments", 12));
    }

    [Fact]
    public async Task Reset_AnApplicationThatRefuses_ReportsItsReason()
    {
        _estate.Answer(HttpStatusCode.Conflict, """{"detail":"The subscription trip-events/trip-dispatch holds unprocessed messages."}""");

        var result = await Client().ResetAsync(EstateApp.Tms, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Refusal.Should().Be("The subscription trip-events/trip-dispatch holds unprocessed messages.");
    }

    [Fact]
    public async Task Reset_AnApplicationThatRefusesTheCredential_SaysSo()
    {
        _estate.Answer(HttpStatusCode.Unauthorized, string.Empty);

        var result = await Client().ResetAsync(EstateApp.PartnerPortal, CancellationToken.None);

        result.Refusal.Should().Be("It refused the Simulator's credential.");
    }

    [Fact]
    public async Task Reset_AnErrorWithoutAProblem_NamesTheStatus()
    {
        _estate.Answer(HttpStatusCode.InternalServerError, "not json");

        var result = await Client().ResetAsync(EstateApp.DriverApp, CancellationToken.None);

        result.Refusal.Should().Be("It answered 500.");
    }

    [Fact]
    public async Task Reset_AnApplicationThatDoesNotAnswer_IsRefused()
    {
        _estate.Fail(new HttpRequestException("down"));

        var result = await Client().ResetAsync(EstateApp.Tms, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Refusal.Should().Be("The TMS didn't answer.");
    }

    [Fact]
    public async Task Reset_PartnerPortalNotConfigured_IsRefusedWithoutACall()
    {
        var result = await Client(new PartnerPortalResetOptions()).ResetAsync(EstateApp.PartnerPortal, CancellationToken.None);

        result.Refusal.Should().Contain("Partner portal");
        _estate.Request.Should().BeNull();
    }
}
