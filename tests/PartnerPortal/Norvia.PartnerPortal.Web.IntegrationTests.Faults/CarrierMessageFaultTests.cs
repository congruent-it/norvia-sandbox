extern alias PortalWeb;

using System.Text;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using PortalWeb::Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>
/// The faults that change what a Carrier Persona sends (P4 to P6), through the real path: the Persona's message goes through the Partner API's stand-in and the workers to the TMS,
/// and the estate settles it the way it settles it in Azure. The Persona believes it succeeded, so each test reads the effect where the estate leaves it: the dead-letter queue and the
/// TMS. Each test books a Shipment of its own and switches its fault off at the end.
/// </summary>
public sealed class CarrierMessageFaultTests(FaultsFixture fixture)
{
    private static readonly PersonaId Carrier = new(FaultsFixture.CarrierPersona);
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task AnUnmappedTrackingCode_IsAccepted202_ThenDeadLetteredAsUnmappable_AndTheShipmentStaysInTransit()
    {
        var shipment = await Journeys.InTransitAsync(Ct);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.TrackingUnmappedCode, Ct);

        var sent = await fixture.Portal.SendTrackingEventAsync(Carrier, shipment.CarrierBookingNumber, TrackingEventType.Delivered, Ct);

        sent!.Reply.Result.Should().Be(TrackingResult.Accepted, "API Management answers 202 and queues the message, whatever it holds");
        var deadLettered = await fixture.DeadLetteredAsync(FaultsFixture.TrackingQueue, message => IsSourceMessage(message, sent.Event.EventId), Ct);
        deadLettered.DeadLetterReason.Should().Be("UnmappableTrackingMessage");
        (await fixture.Tms.ShipmentAsync(shipment.ShipmentNumber, Ct))["status"]!.GetValue<string>().Should().Be("InTransit");
        (await fixture.Tms.TimelineAsync(shipment.ShipmentNumber, Ct))["trackingEvents"]!.AsArray()
            .Select(trackingEvent => trackingEvent!["type"]!.GetValue<string>()).Should().NotContain("Delivered");
        HopOf(PartnerPortalFaultNames.TrackingUnmappedCode).Should().Contain(keys => keys["outcome"] as string == "Succeeded", "the portal's hop succeeded: it is the Logic App's run that fails");
    }

    [Fact]
    public async Task AnUnmappedDeclineReason_FailsTheAnswer400_AndTheBookingStaysRequested()
    {
        var requested = await Journeys.RequestedAsync(Ct);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.AnswerUnmappedReason, Ct);

        var answered = await fixture.Portal.AnswerCarrierBookingAsync(Carrier, requested.CarrierBookingNumber, new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity), Ct);

        answered!.Reply!.Result.Should().Be(CarrierAnswerResult.Invalid, "the Carrier-answer map can't read the reason code");
        answered.Request.Status.Should().Be(CarrierBookingRequestStatus.Open, "the Carrier hears its answer is unreadable, so the inbox keeps the request open");
        (await Journeys.BookingAsync(requested, "Requested", Ct))["answeredAt"].Should().BeNull();
        HopOf(PartnerPortalFaultNames.AnswerUnmappedReason).Should().Contain(keys => keys["outcome"] as string == "Refused" && keys["failureCode"] as string == "CarrierAnswerUnreadable");
    }

    [Fact]
    public async Task AHeicPhoto_IsAccepted202_ThenRefusedByTheTms_AndTheMessageIsDeadLettered()
    {
        var shipment = await Journeys.DeliveredAsync(Ct);
        await using var fault = await fixture.SwitchedOnAsync(PartnerPortalFaultNames.ProofHeicPhoto, Ct);
        var draft = new ProofOfDeliveryDraft("S. de Vries", ProofOfDeliveryCondition.Clean, null, [new ProofOfDeliveryUpload("pod.pdf", "application/pdf", Pdf)]);

        var sent = await fixture.Portal.SendProofOfDeliveryAsync(Carrier, shipment.CarrierBookingNumber, draft, Ct);

        sent!.Reply.Result.Should().Be(TrackingResult.Accepted, "API Management answers 202 and queues the message, whatever its files are");
        var deadLettered = await fixture.DeadLetteredAsync(FaultsFixture.ProofOfDeliveryQueue, message => IsSourceMessage(message, sent.ProofOfDelivery.DocumentId), Ct);
        deadLettered.DeadLetterReason.Should().Be("FileNotAccepted");
        (await fixture.Tms.ProofOfDeliveryAsync(shipment.ShipmentNumber, Ct)).Should().BeNull("the TMS refused the file by its content, so it kept no Proof of delivery");
        HopOf(PartnerPortalFaultNames.ProofHeicPhoto).Should().Contain(keys => keys["outcome"] as string == "Succeeded");
    }

    // The stand-in keeps the Carrier's own message ID on the queued message, as API Management does.
    private static bool IsSourceMessage(Azure.Messaging.ServiceBus.ServiceBusReceivedMessage message, string sourceId) =>
        message.ApplicationProperties.Values.Any(value => value is string text && text == sourceId);

    // What the portal logged for the fault, hop by hop.
    private IEnumerable<IReadOnlyDictionary<string, object?>> HopOf(string fault) =>
        fixture.Telemetry.Records.Where(keys => keys.TryGetValue("fault", out var name) && name as string == fault);
}
