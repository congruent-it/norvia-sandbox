using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;
using Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.List;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperTracking;

public sealed class ShipperTrackingHandlerTests
{
    private static readonly ShipperTrackingSummary Summary = new(
        "TO-0000042", "BD-1", ShipperTrackingStatus.Scheduled, new ShipperPlace("Rotterdam", "NL"), new ShipperPlace("Utrecht", "NL"), Eta: null, LastUpdate: null);

    private static readonly ShipperTrackingDetail Detail = new(
        "TO-0000042", "BD-1", ShipperTrackingStatus.Scheduled, new ShipperPlace("Rotterdam", "NL"), "01-10-2030 08:00", "01-10-2030 12:00",
        new ShipperPlace("Utrecht", "NL"), "02-10-2030 09:00", "02-10-2030 13:00", Eta: null, LastUpdate: null, Progress: [], ProofOfDelivery: null);

    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();

    public ShipperTrackingHandlerTests()
    {
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
    }

    [Fact]
    public async Task List_ForAShipper_AsksNorviaAsThatShipperWithTheFilter_AndReturnsItsAnswer()
    {
        var filter = new ShipperTrackingFilter(ShipperTrackingStatus.InTransit, new DateOnly(2030, 10, 1), new DateOnly(2030, 10, 31), Page: 2);
        var answer = TrackingAnswer.Found<IReadOnlyList<ShipperTrackingSummary>>([Summary]);
        _partnerApi.ListTransportOrdersAsync(Samples.BrouwerDranken, filter, Arg.Any<CancellationToken>()).Returns(answer);

        var result = await new ListShipperTrackingHandler(_personas, _partnerApi).Handle(new ListShipperTrackingQuery(new PersonaId("brouwer-dranken"), filter), CancellationToken.None);

        result.Should().BeSameAs(answer);
    }

    [Fact]
    public async Task List_WhenNorviaIsUnavailable_PassesTheFailureOn()
    {
        var failure = TrackingAnswer.Unavailable<IReadOnlyList<ShipperTrackingSummary>>("Tracking is temporarily unavailable.", "corr-1");
        _partnerApi.ListTransportOrdersAsync(Arg.Any<Persona>(), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>()).Returns(failure);

        var result = await new ListShipperTrackingHandler(_personas, _partnerApi).Handle(new ListShipperTrackingQuery(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter()), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.Unavailable);
        result.MessageId.Should().Be("corr-1");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("van-dijk-transport")]
    public async Task List_ForAPersonaThatIsNoShipper_IsNotFound_AndAsksNobody(string personaId)
    {
        var result = await new ListShipperTrackingHandler(_personas, _partnerApi).Handle(new ListShipperTrackingQuery(new PersonaId(personaId), new ShipperTrackingFilter()), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().ListTransportOrdersAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_ForAShipper_AsksNorviaForThatTransportOrderAsThatShipper()
    {
        var answer = TrackingAnswer.Found(Detail);
        _partnerApi.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000042", Arg.Any<CancellationToken>()).Returns(answer);

        var result = await new GetShipperTrackingHandler(_personas, _partnerApi).Handle(new GetShipperTrackingQuery(new PersonaId("brouwer-dranken"), "TO-0000042"), CancellationToken.None);

        result.Should().BeSameAs(answer);
    }

    [Fact]
    public async Task Get_ForACarrierPersona_IsNotFound_AndAsksNobody()
    {
        var result = await new GetShipperTrackingHandler(_personas, _partnerApi).Handle(new GetShipperTrackingQuery(new PersonaId("van-dijk-transport"), "TO-0000042"), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().GetTransportOrderAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Find_ForAShipper_AsksNorviaForTheShipperReferenceAsThatShipper()
    {
        var answer = TrackingAnswer.Found(Detail);
        _partnerApi.FindTransportOrderAsync(Samples.BrouwerDranken, "BD-1", Arg.Any<CancellationToken>()).Returns(answer);

        var result = await new FindShipperTrackingHandler(_personas, _partnerApi).Handle(new FindShipperTrackingQuery(new PersonaId("brouwer-dranken"), "BD-1"), CancellationToken.None);

        result.Should().BeSameAs(answer);
    }

    [Fact]
    public async Task Find_ForACarrierPersona_IsNotFound_AndAsksNobody()
    {
        var result = await new FindShipperTrackingHandler(_personas, _partnerApi).Handle(new FindShipperTrackingQuery(new PersonaId("van-dijk-transport"), "BD-1"), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().FindTransportOrderAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DownloadFile_ForAShipper_AsksNorviaForTheFileAsThatShipper()
    {
        var fileId = Guid.CreateVersion7();
        var answer = TrackingAnswer.Found(new ShipperProofFileContent("cmr.pdf", "application/pdf", [1, 2, 3]));
        _partnerApi.DownloadProofOfDeliveryFileAsync(Samples.BrouwerDranken, "TO-0000042", fileId, Arg.Any<CancellationToken>()).Returns(answer);

        var result = await new DownloadProofOfDeliveryFileHandler(_personas, _partnerApi)
            .Handle(new DownloadProofOfDeliveryFileQuery(new PersonaId("brouwer-dranken"), "TO-0000042", fileId), CancellationToken.None);

        result.Should().BeSameAs(answer);
    }

    [Fact]
    public async Task DownloadFile_ForACarrierPersona_IsNotFound_AndAsksNobody()
    {
        var result = await new DownloadProofOfDeliveryFileHandler(_personas, _partnerApi)
            .Handle(new DownloadProofOfDeliveryFileQuery(new PersonaId("van-dijk-transport"), "TO-0000042", Guid.CreateVersion7()), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.NotFound);
        await _partnerApi.DidNotReceiveWithAnyArgs().DownloadProofOfDeliveryFileAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Failed_WithAFoundOutcome_Throws() =>
        ((Action)(() => TrackingAnswer.Failed<string>(TrackingOutcome.Found, "No."))).Should().Throw<ArgumentException>();

    [Fact]
    public void Found_HasItsValue_AndNoText()
    {
        var answer = TrackingAnswer.Found("value", "corr-1");

        (answer.IsFound, answer.Value, answer.Text, answer.MessageId).Should().Be((true, "value", null, "corr-1"));
    }

    [Fact]
    public void ShipperPlace_ShowsWhatItHas()
    {
        new[] { new ShipperPlace("Utrecht", "NL"), new ShipperPlace("Utrecht", null), new ShipperPlace(null, "NL"), new ShipperPlace(null, null) }
            .Select(place => place.ToString()).Should().Equal("Utrecht, NL", "Utrecht", "NL", "");
    }

    [Fact]
    public void Persona_WithoutAShipperProfile_PollsEveryFiveSeconds() =>
        Samples.BrouwerDranken.TrackingPollInterval.Should().Be(TimeSpan.FromSeconds(5));

    [Fact]
    public void Persona_WithAShipperProfile_PollsAtItsInterval() =>
        (Samples.BrouwerDranken with { Shipper = new ShipperProfile(TimeSpan.FromSeconds(2)) }).TrackingPollInterval.Should().Be(TimeSpan.FromSeconds(2));

    [Fact]
    public void PersonaView_OfAShipper_CarriesItsPollInterval_AndOfACarrierNone()
    {
        PersonaView.From(Samples.BrouwerDranken with { Shipper = new ShipperProfile(TimeSpan.FromSeconds(2)) }).TrackingPollInterval.Should().Be(TimeSpan.FromSeconds(2));
        PersonaView.From(Samples.VanDijkTransport).TrackingPollInterval.Should().BeNull();
    }
}
