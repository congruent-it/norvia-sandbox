using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestCancellationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    private static CarrierBookingRequest Received() => CarrierBookingRequest.Receive(
        new PersonaId("van-dijk-transport"), "CB-0000001", "SH-0000042", "release-1", At.AddDays(-1),
        new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null), null, []));

    private static CarrierBookingRequest Confirmed()
    {
        var request = Received();
        request.Answer(new CarrierAnswer.Confirmation("VDT-77812"), "answer-1", At.AddHours(-20));
        return request;
    }

    [Fact]
    public void CancelledByNorvia_AConfirmedRequest_ShowsAsCancelled_WithTheReason()
    {
        var request = Confirmed();

        request.CancelledByNorvia("Truck broke down", At).Should().BeTrue();

        request.Status.Should().Be(CarrierBookingRequestStatus.CancelledByNorvia);
        request.CancellationText.Should().Be("Truck broke down");
        request.CancelledAt.Should().Be(At);
        request.CarrierBookingReference.Should().Be("VDT-77812");
    }

    [Fact]
    public void CancelledByNorvia_StopsTheAutomaticTrackingEvents()
    {
        var request = Confirmed();
        request.ScheduleAutoProgress(TimeSpan.FromSeconds(5), At);

        request.CancelledByNorvia("Truck broke down", At);

        request.AutoProgressDueAt.Should().BeNull();
        request.AutoProgressDue(At.AddHours(1)).Should().BeNull();
    }

    [Fact]
    public void CancelledByNorvia_ACancelledRequest_IsLeftAsItWas()
    {
        var request = Confirmed();
        request.CancelledByNorvia("Truck broke down", At);

        request.CancelledByNorvia("Another reason", At.AddHours(1)).Should().BeFalse();

        request.CancellationText.Should().Be("Truck broke down");
        request.CancelledAt.Should().Be(At);
    }

    [Fact]
    public void CancelledByNorvia_ARequestThatIsNotConfirmed_IsLeftAsItWas()
    {
        var request = Received();

        request.CancelledByNorvia("Truck broke down", At).Should().BeFalse();

        request.Status.Should().Be(CarrierBookingRequestStatus.Open);
        request.CancellationText.Should().BeNull();
    }

    [Fact]
    public void CancelledByNorvia_WithoutAReason_Throws()
    {
        var request = Confirmed();

        var act = () => request.CancelledByNorvia(" ", At);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ACancelledRequest_IsNoLongerAShipmentToTrack()
    {
        var kept = Confirmed();
        var cancelled = Confirmed();
        cancelled.CancelledByNorvia("Truck broke down", At);
        var spec = new CarrierShipmentsOfPersonaSpec(new PersonaId("van-dijk-transport"));

        spec.Evaluate([kept, cancelled]).Should().ContainSingle().Which.Should().BeSameAs(kept);
    }

    [Fact]
    public void EndedByNorvia_ACancellation_IsAppliedOnce_ThenRepeated()
    {
        var request = Confirmed();

        request.EndedByNorvia(CarrierCancellationKind.Cancelled, "Truck broke down", "c-1", At).Should().Be(CarrierCancellationOutcome.Applied);
        request.EndedByNorvia(CarrierCancellationKind.Cancelled, "Truck broke down", "c-1", At.AddHours(1)).Should().Be(CarrierCancellationOutcome.Repeated);
        request.CancelledAt.Should().Be(At);
    }

    [Fact]
    public void EndedByNorvia_AWithdrawal_IsAppliedOnce_ThenRepeated()
    {
        var request = Received();

        request.EndedByNorvia(CarrierCancellationKind.Withdrawn, null, "w-1", At).Should().Be(CarrierCancellationOutcome.Applied);
        request.EndedByNorvia(CarrierCancellationKind.Withdrawn, null, "w-1", At.AddHours(1)).Should().Be(CarrierCancellationOutcome.Repeated);
        request.ClosedAt.Should().Be(At);
    }

    [Fact]
    public void EndedByNorvia_AMessageThatDoesNotFitTheState_ChangesNothing()
    {
        var confirmed = Confirmed();
        var open = Received();

        confirmed.EndedByNorvia(CarrierCancellationKind.Withdrawn, null, "w-1", At).Should().Be(CarrierCancellationOutcome.NotApplicable);
        open.EndedByNorvia(CarrierCancellationKind.Cancelled, "Why", "c-1", At).Should().Be(CarrierCancellationOutcome.NotApplicable);

        confirmed.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        open.Status.Should().Be(CarrierBookingRequestStatus.Open);
    }
}
