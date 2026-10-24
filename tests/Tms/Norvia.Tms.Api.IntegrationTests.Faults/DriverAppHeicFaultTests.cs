using System.Net;
using System.Net.Http.Json;
using Norvia.AppHost;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The Driver app's HEIC photo fault (P12): a Proof of delivery the Driver app records stores its photo as HEIC, the TMS refuses the file by its content with <c>FileNotAccepted</c>,
/// and the Driver sees the Proof of delivery Refused. The Delivered report itself is recorded; the contrast, a Proof of delivery recorded with the fault off, is in the TMS unavailable fault's tests. The fault is the Driver app's, switched by the presenter panel with the Demo.Fault role.
/// </summary>
public sealed class DriverAppHeicFaultTests(FaultsFixture fixture)
{
    private const string Fault = "driver-app-heic-photo";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task TheFault_IsListedOffByDefault_AndOnlyTheFaultRoleMaySwitchIt()
    {
        (await FaultsFixture.StatusAsync(fixture.DriverAppPresenter, Fault, Ct)).GetProperty("on").GetBoolean().Should().BeFalse();
        using var anonymous = fixture.DriverAppClient(token: null);
        using var driver = fixture.Driver(FaultsFixture.SecondTestDriver);

        using var withoutToken = await anonymous.PutAsJsonAsync($"/v1/faults/{Fault}", new { on = true, durationSeconds = 60 }, Ct);
        using var withoutRole = await driver.PutAsJsonAsync($"/v1/faults/{Fault}", new { on = true, durationSeconds = 60 }, Ct);

        withoutToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        withoutRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FaultsFixture.StatusAsync(fixture.DriverAppPresenter, Fault, Ct)).GetProperty("on").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task WithTheFaultOn_TheTmsRefusesTheProofOfDeliveryWithFileNotAccepted_AndTheDriverSeesItRefused()
    {
        var trip = await Journeys.DispatchedTripAsync(FaultsFixture.SecondTestDriver, "VEH-0001", Ct);
        await Journeys.StartedAsync(trip, Ct);
        await Journeys.CollectedAsync(trip, Ct);
        await Journeys.ReportStateAsync(trip, "Collected", "Recorded", Ct);
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.DriverAppPresenter, Fault, TimeSpan.FromMinutes(2), Ct);

        var correlationId = await Journeys.DeliveredAsync(trip, photos: 1, Ct);

        var refused = await Journeys.ReportStateAsync(trip, "ProofOfDelivery", "Refused", Ct);
        refused["failureCode"]!.GetValue<string>().Should().Be("FileNotAccepted");
        refused["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        await Journeys.ReportStateAsync(trip, "Delivered", "Recorded", Ct);
        (await Journeys.ShipmentStatusAsync(trip.ShipmentNumber, Ct)).Should().Be("Delivered", "the Delivered report is separate from the Proof of delivery");
        (await fixture.GetTimelineAsync(trip.ShipmentNumber, Ct))["proofsOfDelivery"]!.AsArray().Should().BeEmpty("the TMS kept no Proof of delivery");
        correlationId.Should().NotBeNullOrEmpty();
    }
}
