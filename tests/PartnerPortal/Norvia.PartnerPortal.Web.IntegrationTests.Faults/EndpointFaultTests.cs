using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>
/// The faults that take a Persona's endpoint down (P4 and P7): the workers send to the portal's endpoint, which answers 503 and keeps nothing, and report to the TMS what became of the
/// message. Each test books a Shipment of its own and switches its fault off at the end.
/// </summary>
public sealed class EndpointFaultTests(FaultsFixture fixture)
{
    private static readonly PersonaId Carrier = new(FaultsFixture.CarrierPersona);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task ACarrierBookingEndpointDown_EndsTheBookingNotDelivered_AndTheInboxKeepsNothing()
    {
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.BookingEndpointDown, Ct);

        var released = await Journeys.ReleasedAsync(Ct);

        var booking = await Journeys.BookingAsync(released, "NotDelivered", Ct);
        booking["notDeliveredStatusCode"]!.GetValue<int>().Should().Be(503);
        (await fixture.Portal.ListCarrierBookingRequestsAsync(Carrier, Ct)).Should().NotContain(request => request.CarrierBookingNumber == released.CarrierBookingNumber);
        FailedHop(PartnerPortalFaultNames.BookingEndpointDown, "CarrierEndpointUnavailable").Should().NotBeEmpty();
    }

    [Fact]
    public async Task AShipperNotificationEndpointDown_EndsTheNotificationNotDelivered()
    {
        var shipment = await Journeys.ConfirmedAsync(Ct);
        var sent = await fixture.Portal.SendTrackingEventAsync(Carrier, shipment.CarrierBookingNumber, TrackingEventType.CollectionFailed, Ct);
        sent!.Reply.Result.Should().Be(TrackingResult.Accepted);
        var exception = await OpenExceptionAsync(shipment);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.ShipperEndpointDown, Ct);

        await fixture.Tms.NotifyShipperAsync(exception, "The Carrier couldn't collect.", Ct);

        var notification = await Eventually.Async(
            async () => (await NotificationsAsync(shipment, exception)).SingleOrDefault(candidate => candidate["status"]!.GetValue<string>() == "NotDelivered"), Ct);
        notification["notDeliveredMessage"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        (await fixture.Portal.ListShipperNotificationsAsync(new PersonaId(FaultsFixture.ShipperPersona), Ct)).Should().NotContain(
            inbox => inbox.Message == "The Carrier couldn't collect.", "the endpoint kept nothing");
        FailedHop(PartnerPortalFaultNames.ShipperEndpointDown, "ShipperEndpointUnavailable").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ACarrierCancellationEndpointDown_FlagsTheCarrierNotTold()
    {
        var requested = await Journeys.RequestedAsync(Ct);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.CancellationEndpointDown, Ct);

        await fixture.Tms.WithdrawAsync(requested.CarrierBookingNumber, Ct);

        var outcome = await Eventually.Async(
            async () => (await fixture.Tms.ShipmentsToReleaseAsync(Ct)).OfType<JsonObject>()
                .SingleOrDefault(shipment => shipment["shipmentNumber"]!.GetValue<string>() == requested.ShipmentNumber)?["lastCarrierBookingOutcome"]?.AsObject()
                is { } last && last["carrierNotTold"]!.GetValue<bool>() ? last : null,
            Ct);
        outcome["status"]!.GetValue<string>().Should().Be("Withdrawn");
        outcome["carrierNotToldMessage"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        FailedHop(PartnerPortalFaultNames.CancellationEndpointDown, "CarrierEndpointUnavailable").Should().NotBeEmpty();
    }

    private async Task<string> OpenExceptionAsync(BookedShipment shipment) =>
        (await Eventually.Async(
            async () => (await fixture.Tms.TimelineAsync(shipment.ShipmentNumber, Ct))["exceptions"]!.AsArray().OfType<JsonObject>()
                .FirstOrDefault(exception => exception["type"]!.GetValue<string>() == "CollectionFailed"),
            Ct))["exceptionNumber"]!.GetValue<string>();

    private async Task<IReadOnlyList<JsonObject>> NotificationsAsync(BookedShipment shipment, string exceptionNumber) =>
        [.. (await fixture.Tms.TimelineAsync(shipment.ShipmentNumber, Ct))["exceptions"]!.AsArray().OfType<JsonObject>()
            .Single(exception => exception["exceptionNumber"]!.GetValue<string>() == exceptionNumber)["notifications"]!.AsArray().OfType<JsonObject>()];

    // The hop the fault made fail, as the portal logged it: Failed, with the failure code and the fault's name.
    private IEnumerable<IReadOnlyDictionary<string, object?>> FailedHop(string fault, string failureCode) =>
        fixture.Telemetry.Records.Where(keys => keys.TryGetValue("fault", out var name) && name as string == fault
            && keys["outcome"] as string == "Failed" && keys["failureCode"] as string == failureCode);
}
