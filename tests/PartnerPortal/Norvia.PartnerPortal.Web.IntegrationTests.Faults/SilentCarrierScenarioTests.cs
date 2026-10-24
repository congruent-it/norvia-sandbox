using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>
/// The runtime part of incident 1, the silent Carrier (docs/operations/as-found.md): the Carrier posts "delivered" with a code the map can't read, is told 202, and the Shipment stays In transit.
/// Nobody is told, so the one way to find it is the Integration console, which reads the dead-letter queue by the correlation ID the Carrier was given. The alerts and the Stale thresholds
/// the preset removes are Azure's, and the post-deploy smoke run checks them. The test books a Shipment of its own.
/// It peeks the queue through the console's adapter: the local Service Bus emulator reports 0 for every runtime count, so the adapter's counts and its search by correlation ID, which skip a
/// queue that counts nothing, can't find the message locally (the console's own integration tests skip them for that reason).
/// </summary>
public sealed class SilentCarrierScenarioTests(FaultsFixture fixture)
{
    private static readonly PersonaId Carrier = new(FaultsFixture.CarrierPersona);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task TheCarrierPostsDelivered_IsTold202_AndTheConsoleFindsTheDeadLetteredMessageByTheCorrelationIdItWasGiven()
    {
        var shipment = await Journeys.InTransitAsync(Ct);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.TrackingUnmappedCode, Ct);

        var sent = await fixture.Portal.SendTrackingEventAsync(Carrier, shipment.CarrierBookingNumber, TrackingEventType.Delivered, Ct);

        sent!.Reply.Result.Should().Be(TrackingResult.Accepted, "the Carrier believes it is done");
        var correlationId = sent.Reply.MessageId;
        correlationId.Should().NotBeNullOrWhiteSpace("the correlation ID is what the Carrier gives Norvia to find the message");
        var found = await Eventually.Async(
            async () => (await fixture.ConsoleDeadLetters.PeekAsync(FaultsFixture.TrackingQueue, 100, Ct))?.FirstOrDefault(message => message.CorrelationId == correlationId), Ct);
        found.Reason.Should().Be("UnmappableTrackingMessage");
        (await fixture.Tms.ShipmentAsync(shipment.ShipmentNumber, Ct))["status"]!.GetValue<string>().Should().Be("InTransit", "the Shipper's screen stays on In transit");
    }
}
