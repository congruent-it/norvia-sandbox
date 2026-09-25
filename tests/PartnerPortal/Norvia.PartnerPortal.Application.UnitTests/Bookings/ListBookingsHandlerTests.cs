using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Bookings;

public sealed class ListBookingsHandlerTests
{
    private readonly IReadRepository<Booking> _repository = Substitute.For<IReadRepository<Booking>>();

    [Fact]
    public async Task Handle_ReadsThePersonasBookings_WithTheirAnswers()
    {
        var accepted = Booking.Record(Samples.BrouwerDranken.Id, "BD-1", Samples.Now, NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        var refused = Booking.Record(Samples.BrouwerDranken.Id, "BD-2", Samples.Now, NorviaAnswer.Refused("No.", "corr-2"));
        _repository.ListAsync(Arg.Is<BookingsOfPersonaSpec>(spec => spec.PersonaId == Samples.BrouwerDranken.Id), Arg.Any<CancellationToken>())
            .Returns([accepted, refused]);

        var bookings = await new ListBookingsHandler(_repository).Handle(new ListBookingsQuery("brouwer-dranken"), CancellationToken.None);

        bookings.Should().SatisfyRespectively(
            first =>
            {
                first.Id.Should().Be(accepted.Id.Value);
                first.ShipperReference.Should().Be("BD-1");
                first.SentAt.Should().Be(Samples.Now);
                first.Result.Should().Be(BookingResult.Accepted);
                first.TransportOrderNumber.Should().Be("TO-0000042");
            },
            second =>
            {
                second.Result.Should().Be(BookingResult.Refused);
                second.Text.Should().Be("No.");
                second.MessageId.Should().Be("corr-2");
            });
    }
}
