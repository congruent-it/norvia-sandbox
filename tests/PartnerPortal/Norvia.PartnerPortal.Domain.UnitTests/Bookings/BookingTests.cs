using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.Bookings;

public sealed class BookingTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_KeepsWhatWasSentAndNorviasAnswer()
    {
        var answer = NorviaAnswer.Accepted("TO-0000042", "corr-1");

        var booking = Booking.Record(new PersonaId("brouwer-dranken"), "BD-2030-0001", SentAt, answer);

        booking.PersonaId.Should().Be(new PersonaId("brouwer-dranken"));
        booking.ShipperReference.Should().Be("BD-2030-0001");
        booking.SentAt.Should().Be(SentAt);
        booking.Answer.Should().Be(answer);
    }

    [Fact]
    public void Record_WithoutAShipperReference_IsKept_SoEveryAttemptIsInTheList()
    {
        var booking = Booking.Record(new PersonaId("brouwer-dranken"), shipperReference: null, SentAt, NorviaAnswer.NotReceived(null));

        booking.ShipperReference.Should().BeNull();
    }

    [Fact]
    public void Record_GivesEachBookingAnIdOfItsOwn()
    {
        var first = Booking.Record(new PersonaId("brouwer-dranken"), "BD-1", SentAt, NorviaAnswer.NotReceived(null));
        var second = Booking.Record(new PersonaId("brouwer-dranken"), "BD-1", SentAt, NorviaAnswer.NotReceived(null));

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void BookingsOfPersonaSpec_ListsOnlyThatPersonasBookings_LatestFirst()
    {
        var brouwer = new PersonaId("brouwer-dranken");
        var earlier = Booking.Record(brouwer, "BD-1", SentAt, NorviaAnswer.NotReceived(null));
        var later = Booking.Record(brouwer, "BD-2", SentAt.AddMinutes(5), NorviaAnswer.NotReceived(null));
        var someoneElses = Booking.Record(new PersonaId("van-dijk-transport"), "VD-1", SentAt, NorviaAnswer.NotReceived(null));

        var listed = new BookingsOfPersonaSpec(brouwer).Evaluate([earlier, someoneElses, later]);

        listed.Should().Equal(later, earlier);
    }
}
