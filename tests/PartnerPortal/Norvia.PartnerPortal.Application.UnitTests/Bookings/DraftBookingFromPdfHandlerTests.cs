using Ardalis.Result;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Bookings;

public sealed class DraftBookingFromPdfHandlerTests
{
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();
    private readonly DraftBookingFromPdfHandler _handler;

    public DraftBookingFromPdfHandlerTests()
    {
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _handler = new DraftBookingFromPdfHandler(_personas, _partnerApi);
    }

    [Fact]
    public async Task Handle_AsAKnownPersona_SendsThePdfToNorviaAsThatPersona()
    {
        byte[] pdf = [1, 2, 3];
        var answer = BookingDraftAnswer.Refused(BookingDraftOutcome.NoTextInPdf, "corr-1");
        _partnerApi.DraftBookingFromPdfAsync(Samples.BrouwerDranken, pdf, Arg.Any<CancellationToken>()).Returns(answer);

        var result = await _handler.Handle(new DraftBookingFromPdfQuery(Samples.BrouwerDranken.Id, pdf), CancellationToken.None);

        result.Value.Should().Be(answer);
    }

    [Fact]
    public async Task Handle_ForAPersonaThereIsNone_IsNotFound_AndAsksNobody()
    {
        var result = await _handler.Handle(new DraftBookingFromPdfQuery(new PersonaId("nobody"), [1]), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().DraftBookingFromPdfAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
