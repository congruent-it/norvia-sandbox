using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>
/// The TMS creates a Carrier booking at release (P4), Requested, waiting for the Carrier's answer, then records the
/// Carrier's confirmation or rejection. Sent/not-delivered reporting and withdrawal are later tickets.
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

    [Fact]
    public void IsRepeatOfConfirm_TheSameReferenceAndPlannedCollectionTime_IsTrue()
    {
        var booking = Requested();
        var plannedCollectionTime = AnsweredAt.AddDays(1);
        booking.Confirm("VDT-240917-0042", plannedCollectionTime, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        booking.IsRepeatOfConfirm("VDT-240917-0042", plannedCollectionTime).Should().BeTrue();
    }

    [Theory]
    [InlineData("VDT-240917-9999", null)]
    [InlineData("VDT-240917-0042", 1)]
    public void IsRepeatOfConfirm_ADifferentReferenceOrPlannedCollectionTime_IsFalse(string reference, int? plannedCollectionTimeOffsetDays)
    {
        var booking = Requested();
        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        booking.IsRepeatOfConfirm(reference, plannedCollectionTimeOffsetDays is { } days ? AnsweredAt.AddDays(days) : null).Should().BeFalse();
    }

    [Fact]
    public void IsRepeatOfConfirm_ARejectedBooking_IsFalse()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, Answer, AnsweredAt);

        booking.IsRepeatOfConfirm("VDT-240917-0042", plannedCollectionTime: null).Should().BeFalse();
    }

    [Fact]
    public void IsRepeatOfReject_TheSameReasonAndText_IsTrue()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.", Answer, AnsweredAt);

        booking.IsRepeatOfReject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.").Should().BeTrue();
    }

    [Theory]
    [InlineData(CarrierBookingRejectionReason.LaneNotServed, "No trucks on Friday.")]
    [InlineData(CarrierBookingRejectionReason.NoCapacity, "A different reason.")]
    [InlineData(CarrierBookingRejectionReason.NoCapacity, null)]
    public void IsRepeatOfReject_ADifferentReasonOrText_IsFalse(CarrierBookingRejectionReason reason, string? reasonText)
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.", Answer, AnsweredAt);

        booking.IsRepeatOfReject(reason, reasonText).Should().BeFalse();
    }

    [Fact]
    public void IsRepeatOfReject_AConfirmedBooking_IsFalse()
    {
        var booking = Requested();
        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);

        booking.IsRepeatOfReject(CarrierBookingRejectionReason.NoCapacity, reasonText: null).Should().BeFalse();
    }

    [Fact]
    public void MarkSent_ARequestedBooking_RecordsTheSentTime_AndRaisesNoDomainEvent()
    {
        var booking = Requested();
        var sentAt = RequestedAt.AddMinutes(5);

        var sent = booking.MarkSent(sentAt);

        sent.Should().BeTrue();
        booking.SentAt.Should().Be(sentAt);
        booking.Status.Should().Be(CarrierBookingStatus.Requested, "a sent report doesn't change the booking's status");
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkSent_ABookingThatIsNoLongerRequested_ChangesNothing()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, Answer, AnsweredAt);
        booking.ClearDomainEvents();

        var sent = booking.MarkSent(AnsweredAt.AddMinutes(1));

        sent.Should().BeFalse();
        booking.SentAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_ARequestedBooking_MovesItToNotDelivered_WithTheTechnicalCause()
    {
        var booking = Requested();
        var reportedAt = RequestedAt.AddMinutes(10);
        var reportCorrelationId = new CorrelationId("corr-report");

        var reported = booking.ReportNotDelivered(503, "Service unavailable.", reportCorrelationId, reportedAt);

        reported.Should().BeTrue();
        booking.Status.Should().Be(CarrierBookingStatus.NotDelivered);
        booking.NotDeliveredStatusCode.Should().Be(503);
        booking.NotDeliveredMessage.Should().Be("Service unavailable.");
        booking.NotDeliveredAt.Should().Be(reportedAt);
    }

    [Fact]
    public void ReportNotDelivered_ARequestedBooking_RaisesCarrierBookingNotDelivered_WithTheReportsCorrelationId()
    {
        var booking = Requested();
        var reportCorrelationId = new CorrelationId("corr-report");

        booking.ReportNotDelivered(503, "Service unavailable.", reportCorrelationId, RequestedAt.AddMinutes(10));

        var notDelivered = booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingNotDeliveredEvent>().Subject;
        notDelivered.Booking.Should().BeSameAs(booking);
        notDelivered.CorrelationId.Should().Be(reportCorrelationId);
    }

    [Fact]
    public void ReportNotDelivered_ABookingThatIsNoLongerRequested_ChangesNothing()
    {
        var booking = Requested();
        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, TransportOrderNumberOfTwelve, Answer, AnsweredAt);
        booking.ClearDomainEvents();

        var reported = booking.ReportNotDelivered(503, "Service unavailable.", new CorrelationId("corr-report"), AnsweredAt.AddMinutes(1));

        reported.Should().BeFalse();
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed);
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Withdraw_ARequestedBooking_MovesItToWithdrawn()
    {
        var booking = Requested();
        var key = new IdempotencyKey("key-withdraw-1");
        var withdrawCorrelationId = new CorrelationId("corr-withdraw");
        var withdrawnAt = RequestedAt.AddMinutes(15);

        var withdrawn = booking.Withdraw(key, withdrawCorrelationId, withdrawnAt);

        withdrawn.Should().BeTrue();
        booking.Status.Should().Be(CarrierBookingStatus.Withdrawn);
        booking.WithdrawnAt.Should().Be(withdrawnAt);
        booking.WithdrawalUnder(key).Should().Be(withdrawCorrelationId);
    }

    [Fact]
    public void Withdraw_ARequestedBooking_RaisesCarrierBookingWithdrawn()
    {
        var booking = Requested();

        booking.Withdraw(new IdempotencyKey("key-withdraw-1"), new CorrelationId("corr-withdraw"), RequestedAt.AddMinutes(15));

        var withdrawn = booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingWithdrawnEvent>().Subject;
        withdrawn.Booking.Should().BeSameAs(booking);
    }

    [Fact]
    public void Withdraw_ABookingThatIsNoLongerRequested_ChangesNothing()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, Answer, AnsweredAt);
        booking.ClearDomainEvents();

        var withdrawn = booking.Withdraw(new IdempotencyKey("key-withdraw-1"), new CorrelationId("corr-withdraw"), AnsweredAt.AddMinutes(1));

        withdrawn.Should().BeFalse();
        booking.Status.Should().Be(CarrierBookingStatus.Rejected);
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void WithdrawalUnder_AnUnknownKey_IsNull()
    {
        var booking = Requested();
        booking.Withdraw(new IdempotencyKey("key-withdraw-1"), new CorrelationId("corr-withdraw"), RequestedAt.AddMinutes(15));

        booking.WithdrawalUnder(new IdempotencyKey("key-withdraw-2")).Should().BeNull();
    }

    [Fact]
    public void WithdrawalUnder_ABookingNeverWithdrawn_IsNull()
    {
        var booking = Requested();

        booking.WithdrawalUnder(new IdempotencyKey("key-withdraw-1")).Should().BeNull();
    }

    private static CarrierBooking Requested()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);
        booking.ClearDomainEvents();
        return booking;
    }
}
