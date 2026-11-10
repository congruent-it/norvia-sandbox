using Ardalis.Result;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Bookings;

public sealed class DraftBookingFromTextHandlerTests
{
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();
    private readonly DraftBookingFromTextHandler _handler;

    public DraftBookingFromTextHandlerTests()
    {
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _handler = new DraftBookingFromTextHandler(_personas, _partnerApi);
    }

    [Fact]
    public async Task Handle_AsAKnownPersona_AsksNorviaForTheDraftAsThatPersona()
    {
        var answer = new BookingDraftAnswer(BookingDraftOutcome.Drafted, new BookingRequest { ShipperReference = "SH-1" }, [], "corr-1", new Dictionary<string, string>(), []);
        _partnerApi.DraftBookingFromTextAsync(Samples.BrouwerDranken, "the email", Arg.Any<CancellationToken>()).Returns(answer);

        var result = await _handler.Handle(new DraftBookingFromTextQuery(Samples.BrouwerDranken.Id, "the email"), CancellationToken.None);

        result.Value.Should().Be(answer);
    }

    [Fact]
    public async Task Handle_ForAPersonaThereIsNone_IsNotFound_AndAsksNobody()
    {
        var result = await _handler.Handle(new DraftBookingFromTextQuery(new PersonaId("nobody"), "the email"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().DraftBookingFromTextAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
