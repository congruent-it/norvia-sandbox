using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>
/// The TMS creates a Carrier booking at release (P4), Requested, waiting for the Carrier's answer, then records the
/// Carrier's confirmation or rejection. This slice covers only the happy path: an answer for a booking that isn't
/// Requested, field validation, idempotent repeats, sent/not-delivered reporting and withdrawal are later tickets.
/// </summary>
public sealed class CarrierBookingTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CorrelationId Release = new("corr-release");
    private static readonly CorrelationId Answer = new("corr-answer");
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 24, 12, 45, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AnsweredAt = RequestedAt.AddHours(1);
    private static readonly ShipmentId Shipment = ShipmentId.New();
    private static readonly ShipmentNumber ShipmentNumberOfTwelve = ShipmentNumber.FromSequence(12);
    private static readonly TransportOrderNumber TransportOrderNumberOfTwelve = TransportOrderNumber.FromSequence(12);

    [Fact]
    public void Request_ForACarrierShipmentsRelease_IsRequested_WithTheShipmentAndTheCarrier()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);

        booking.Number.Should().Be(CarrierBookingNumber.FromSequence(42));
        booking.ShipmentId.Should().Be(Shipment);
        booking.ShipmentNumber.Should().Be(ShipmentNumberOfTwelve);
        booking.CarrierId.Should().Be(VanDijk);
        booking.Status.Should().Be(CarrierBookingStatus.Requested);
        booking.CorrelationId.Should().Be(Release);
        booking.RequestedAt.Should().Be(RequestedAt);
    }

    [Fact]
    public void Request_RaisesNoDomainEvent_AsCreationIsntAnnouncedSeparatelyFromTheRelease()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);

        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Confirm_ARequestedBooking_IsConfirmed_WithTheReferenceAndThePlannedCollectionTime()
    {
        var booking = Requested();
        var plannedCollectionTime = AnsweredAt.AddDays(1);

        var confirmed = booking.Confirm("VDT-240917-0042", plannedCollectionTime, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        confirmed.Should().BeTrue();
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed);
        booking.CarrierBookingReference.Should().Be("VDT-240917-0042");
        booking.PlannedCollectionTime.Should().Be(plannedCollectionTime);
        booking.CorrelationId.Should().Be(Release, "the booking keeps the release's own trail alongside the answer's");
        booking.AnswerCorrelationId.Should().Be(Answer);
        booking.AnsweredAt.Should().Be(AnsweredAt);
    }

    [Fact]
    public void Confirm_WithNoPlannedCollectionTime_IsConfirmed_WithNoneStored()
    {
        var booking = Requested();

        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        booking.PlannedCollectionTime.Should().BeNull();
    }

    [Fact]
    public void Confirm_ARequestedBooking_RaisesCarrierBookingConfirmed_WithTheTransportOrderNumber()
    {
        var booking = Requested();

        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        var confirmed = booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingConfirmedEvent>().Subject;
        confirmed.Booking.Should().BeSameAs(booking);
        confirmed.TransportOrderNumber.Should().Be(TransportOrderNumberOfTwelve);
    }

    [Fact]
    public void Reject_ARequestedBooking_IsRejected_WithTheReasonAndText()
    {
        var booking = Requested();

        var rejected = booking.Reject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.", Answer, AnsweredAt);

        rejected.Should().BeTrue();
        booking.Status.Should().Be(CarrierBookingStatus.Rejected);
        booking.RejectionReason.Should().Be(CarrierBookingRejectionReason.NoCapacity);
        booking.RejectionReasonText.Should().Be("No trucks on Friday.");
        booking.CorrelationId.Should().Be(Release, "the booking keeps the release's own trail alongside the answer's");
        booking.AnswerCorrelationId.Should().Be(Answer);
        booking.AnsweredAt.Should().Be(AnsweredAt);
    }

    [Fact]
    public void Reject_ARequestedBooking_RaisesCarrierBookingRejected()
    {
        var booking = Requested();

        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, Answer, AnsweredAt);

        var rejected = booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingRejectedEvent>().Subject;
        rejected.Booking.Should().BeSameAs(booking);
    }

    [Fact]
    public void Confirm_ARejectedBooking_ChangesNothing()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, Answer, AnsweredAt);
        booking.ClearDomainEvents();

        var confirmed = booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, new CorrelationId("corr-answer-2"), AnsweredAt);

        confirmed.Should().BeFalse();
        booking.Status.Should().Be(CarrierBookingStatus.Rejected);
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reject_AConfirmedBooking_ChangesNothing()
    {
        var booking = Requested();
        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);
        booking.ClearDomainEvents();

        var rejected = booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, new CorrelationId("corr-answer-2"), AnsweredAt);

        rejected.Should().BeFalse();
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed);
        booking.DomainEvents.Should().BeEmpty();
    }

    private static CarrierBooking Requested()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);
        booking.ClearDomainEvents();
        return booking;
    }
}
