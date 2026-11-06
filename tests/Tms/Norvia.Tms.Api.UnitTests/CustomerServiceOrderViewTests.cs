using System.Text.Json;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The Customer service agent's view of a Transport Order and its Shipment is narrower than the read models (ADR 0040, #423).</summary>
public sealed class CustomerServiceOrderViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    private static TransportOrderView Order(
        RatingResult? rating = RatingResult.Rated, RatingFailureView? failure = null, BillingState? billing = BillingState.NotYetBillable, bool released = false) => new(
        Guid.NewGuid(),
        "TO-0000042",
        TransportOrderStatus.Planned,
        "SH-0000042",
        new ShipperView("SHP-0001", "Brouwer Dranken"),
        IntakeChannel.ShipperApi,
        "corr-9",
        Now,
        [],
        rating,
        null,
        failure,
        new CanonicalTransportOrder(
            "TO-0000042",
            "SHP-0001",
            "BD-2026-0001",
            new CanonicalCollectionDetails(new CanonicalAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"), null),
            new CanonicalDeliveryDetails(
                new CanonicalAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
                new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", "sanne@dezwaan.example"),
                null),
            [new CanonicalGoodsLine("pallet", 2, 850m, null)],
            "Ring at the side entrance"),
        billing,
        released ? new ProofOfDeliveryReleaseView("Jo", Now, "Carrier lost it") : null);

    private static ProofOfDeliveryView Proof(bool current, DeliveryCondition condition = DeliveryCondition.Clean) => new(
        Guid.NewGuid(), "SH-0000042", "CAR-0001", "doc-1", "S. de Vries", Now, "Utrecht", "3511 AW", "NL", condition, "Left with the neighbour", Now, "corr-3",
        [new ProofOfDeliveryFileView(Guid.NewGuid(), "cmr-de-zwaan.pdf", "application/pdf", 9)], current);

    private static ExceptionTimelineEntry Exception(ExceptionStatus status, ExceptionType type = ExceptionType.DeliveryFailed, ShipperNotificationView? notification = null) => new(
        "EX-0000001",
        type,
        status,
        Now,
        false,
        false,
        [new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-1", Now), new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-2", Now)],
        [new ExceptionNoteView("Called the driver; Sanne de Vries was out", "Pia", Now)],
        notification is null ? [] : [notification],
        [],
        status == ExceptionStatus.Resolved ? ExceptionOutcome.Redelivered : null,
        status == ExceptionStatus.Resolved ? "Redelivered on Friday" : null,
        status == ExceptionStatus.Resolved ? Now.AddDays(1) : null);

    private static ShipmentTimeline Timeline(
        IReadOnlyList<ProofOfDeliveryView>? proofs = null, IReadOnlyList<ExceptionTimelineEntry>? exceptions = null, DateTimeOffset? billable = null) => new(
        [
            new TrackingTimelineEntry(TrackingEventType.Collected, Now, "Rotterdam", "3087 BM", "NL", null, "Door was jammed", Now.AddMinutes(1), true, "CAR-0001"),
            new TrackingTimelineEntry(TrackingEventType.Delivered, Now.AddHours(8), "Utrecht", "3511 AW", "NL", Now.AddHours(9), "Handed to Sanne", Now.AddHours(8), false, "Own fleet"),
        ],
        proofs ?? [],
        exceptions ?? [],
        [],
        billable is null ? null : new ShipmentBillingView(BillingState.Billable, billable, null));

    [Fact]
    public void Of_ATransportOrder_NeverCarriesAConsigneeNameAddressContactOrInstructions()
    {
        var json = JsonSerializer.Serialize(CustomerServiceAgentMapping.Of(Order(), ShipmentStatus.Delivered, Timeline()));

        json.Should().NotContainAny("Café De Zwaan", "Sanne", "+31 30 555 0142", "dezwaan", "Oudegracht", "3511 AW", "Waalhaven", "Ring at the side entrance");
    }

    [Fact]
    public void Of_ABillableOrder_CarriesItsStatusesAndNoBlockingReason()
    {
        var view = CustomerServiceAgentMapping.Of(Order(billing: BillingState.Billable), ShipmentStatus.Delivered, Timeline([Proof(true)], billable: Now.AddDays(2)));

        view.Should().BeEquivalentTo(new AgentTransportOrderView(
            "TO-0000042", "Planned", Now, "corr-9", "ShipperApi", new AgentShipperView("SHP-0001", "Brouwer Dranken"), "BD-2026-0001", "SH-0000042", "Delivered",
            new AgentRatingView("Rated", null, null), new AgentBillingView("Billable", Now.AddDays(2), false, [])));
    }

    [Fact]
    public void Of_ADeliveredOrderThatIsNotYetBillable_NamesEveryReasonThatBlocksIt()
    {
        var order = Order(rating: RatingResult.RatingFailed, failure: new RatingFailureView("NoMatchingLane", "No lane matches."));

        var view = CustomerServiceAgentMapping.Of(order, ShipmentStatus.Delivered, Timeline([Proof(false)], [Exception(ExceptionStatus.Open)]));

        view.Rating.Should().Be(new AgentRatingView("RatingFailed", "NoMatchingLane", "No lane matches."));
        view.Billing!.BlockingReasons.Should().Equal("NotRated", "NoProofOfDelivery", "OpenException");
    }

    [Fact]
    public void Of_AnOrderReleasedWithoutAProofOfDelivery_IsNotBlockedByTheMissingOne()
    {
        var view = CustomerServiceAgentMapping.Of(Order(released: true), ShipmentStatus.Delivered, Timeline([], [Exception(ExceptionStatus.Resolved)]));

        view.Billing.Should().BeEquivalentTo(new AgentBillingView("NotYetBillable", null, true, []));
    }

    [Fact]
    public void Of_AnOrderWithNoShipmentOrNoRating_HasNoShipmentAndNoBlockingReasons()
    {
        var order = Order(rating: null, billing: null) with { ShipmentNumber = null };

        var view = CustomerServiceAgentMapping.Of(order, null, null);

        view.Should().BeEquivalentTo(new AgentTransportOrderView(
            "TO-0000042", "Planned", Now, "corr-9", "ShipperApi", new AgentShipperView("SHP-0001", "Brouwer Dranken"), "BD-2026-0001", null, null, null, null));
        CustomerServiceAgentMapping.Of(Order(), ShipmentStatus.Planned, null).Billing!.BlockingReasons.Should().BeEmpty();
    }

    [Fact]
    public void Of_ATimeline_CarriesEachEventWithItsSource_AndNeverTheRemarkOrThePostcode()
    {
        var view = CustomerServiceAgentMapping.Of("SH-0000042", Timeline());

        view.TrackingEvents.Should().Equal(
            new AgentTrackingEventView("Collected", Now, "Rotterdam", "NL", null, Now.AddMinutes(1), true, "CAR-0001"),
            new AgentTrackingEventView("Delivered", Now.AddHours(8), "Utrecht", "NL", Now.AddHours(9), Now.AddHours(8), false, "Own fleet"));
        JsonSerializer.Serialize(view).Should().NotContainAny("Door was jammed", "Handed to Sanne", "3087 BM", "3511 AW");
    }

    [Fact]
    public void ExceptionsOf_CarriesTypeCausesStatusNotesAndWhetherTheShipperWasNotified_NeverTheNotificationText()
    {
        var sent = new ShipperNotificationView("Dear Sanne de Vries, your goods are late", "Pia", ShipperNotificationStatus.Sent, Now, Now.AddHours(1), null);
        var requested = new ShipperNotificationView("Dear Shipper", "Pia", ShipperNotificationStatus.Requested, Now, null, null);

        var view = CustomerServiceAgentMapping.ExceptionsOf(
            "SH-0000042", Timeline(exceptions: [Exception(ExceptionStatus.Open, notification: requested), Exception(ExceptionStatus.Resolved, ExceptionType.Damaged, sent)]));

        view.Exceptions.Should().HaveCount(2);
        view.Exceptions[0].Should().Match<AgentExceptionView>(exception =>
            exception.Type == "DeliveryFailed" && exception.Status == "Open" && !exception.ShipperNotified && exception.ShipperNotifiedAt == null && exception.Outcome == null
            && exception.Causes.Count == 1 && exception.Causes[0] == "TrackingEvent" && exception.Notes.Count == 1);
        view.Exceptions[1].Should().Match<AgentExceptionView>(exception =>
            exception.Status == "Resolved" && exception.ShipperNotified && exception.ShipperNotifiedAt == Now.AddHours(1) && exception.Outcome == "Redelivered" && exception.ResolvedAt == Now.AddDays(1));
        JsonSerializer.Serialize(view).Should().NotContain("your goods are late").And.NotContain("Dear Shipper");
    }

    [Theory]
    [InlineData(true, ShipmentStatus.Delivered, null, "Current")]
    [InlineData(false, ShipmentStatus.InTransit, null, "NotDelivered")]
    [InlineData(false, ShipmentStatus.Delivered, true, "Missing")]
    [InlineData(false, ShipmentStatus.Delivered, false, "NotDueYet")]
    [InlineData(false, ShipmentStatus.Delivered, null, "NotDueYet")]
    public void ProofOfDeliveryOf_SaysWhereTheShipmentStands(bool current, ShipmentStatus status, bool? missing, string expected)
    {
        var timeline = Timeline(current ? [Proof(true)] : []);

        CustomerServiceAgentMapping.ProofOfDeliveryOf("SH-0000042", status, timeline, missing).Status.Should().Be(expected);
    }

    [Fact]
    public void ProofOfDeliveryOf_ListsCurrentAndSupersededWithReservations_NeverWhoSignedARemarkOrAFile()
    {
        var timeline = Timeline([Proof(false), Proof(true, DeliveryCondition.Reservations)]);

        var view = CustomerServiceAgentMapping.ProofOfDeliveryOf("SH-0000042", ShipmentStatus.Delivered, timeline, null);

        view.WithReservations.Should().BeTrue();
        view.ProofsOfDelivery.Should().Equal(
            new AgentProofOfDeliveryView("CAR-0001", Now, "Clean", false, Now, 1),
            new AgentProofOfDeliveryView("CAR-0001", Now, "Reservations", true, Now, 1));
        JsonSerializer.Serialize(view).Should().NotContainAny("S. de Vries", "neighbour", "cmr-de-zwaan", "application/pdf", "doc-1");
        CustomerServiceAgentMapping.ProofOfDeliveryOf("SH-0000042", ShipmentStatus.Delivered, Timeline(), null).WithReservations.Should().BeNull();
    }
}
