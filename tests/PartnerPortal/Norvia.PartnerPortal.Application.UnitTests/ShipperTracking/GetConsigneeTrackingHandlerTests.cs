using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.ConsigneeTracking;
using Norvia.PartnerPortal.Application.ConsigneeTracking.GetView;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperTracking;

public sealed class GetConsigneeTrackingHandlerTests
{
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();

    [Fact]
    public async Task Handle_AsksNorviaWithTheCodeTrimmed_AndReturnsItsAnswer()
    {
        var answer = TrackingAnswer.Found(new ConsigneeTrackingView(ShipperTrackingStatus.Received, new(null, null), new(null, null), null, null, null, null, [], null));
        _partnerApi.GetConsigneeViewAsync("0123456789ABCDEF", "203.0.113.9", Arg.Any<CancellationToken>()).Returns(answer);

        var result = await new GetConsigneeTrackingHandler(_partnerApi).Handle(new GetConsigneeTrackingQuery(" 0123456789ABCDEF ", "203.0.113.9"), CancellationToken.None);

        result.Should().BeSameAs(answer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Handle_ABlankCode_IsNotFound_AndNorviaIsNotAsked(string code)
    {
        var result = await new GetConsigneeTrackingHandler(_partnerApi).Handle(new GetConsigneeTrackingQuery(code), CancellationToken.None);

        result.Outcome.Should().Be(TrackingOutcome.NotFound);
        await _partnerApi.DidNotReceive().GetConsigneeViewAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ToString_NeverShowsTheCode() =>
        new GetConsigneeTrackingQuery("0123456789ABCDEF").ToString().Should().NotContain("0123456789ABCDEF");
}
