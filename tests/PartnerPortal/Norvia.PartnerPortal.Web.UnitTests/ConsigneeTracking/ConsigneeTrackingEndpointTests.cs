using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.ConsigneeTracking;
using Norvia.PartnerPortal.Application.ConsigneeTracking.GetView;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Web.ConsigneeTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ConsigneeTracking;

public sealed class ConsigneeTrackingEndpointTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly DefaultHttpContext _context = new();

    [Fact]
    public async Task Track_AsksNorviaForTheCodeInTheLink_AndHidesTheLinkFromOtherSites()
    {
        _mediator.Send(Arg.Any<GetConsigneeTrackingQuery>(), Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found(new ConsigneeTrackingView(ShipperTrackingStatus.Received, new(null, null), new(null, null), null, null, null, null, [], null)));

        var result = await ConsigneeTrackingEndpoint.TrackAsync("0123456789ABCDEF", _context, _mediator, new FakeTimeProvider(), CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Is<GetConsigneeTrackingQuery>(query => query.TrackingCode == "0123456789ABCDEF"), Arg.Any<CancellationToken>());
        result.StatusCode.Should().Be(200);
        _context.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        _context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Theory]
    [InlineData(TrackingOutcome.NotFound, 404)]
    [InlineData(TrackingOutcome.Refused, 429)]
    [InlineData(TrackingOutcome.Unavailable, 503)]
    public async Task Track_AFailure_HasItsStatus(TrackingOutcome outcome, int status)
    {
        _mediator.Send(Arg.Any<GetConsigneeTrackingQuery>(), Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Failed<ConsigneeTrackingView>(outcome, "text"));

        var result = await ConsigneeTrackingEndpoint.TrackAsync("x", _context, _mediator, new FakeTimeProvider(), CancellationToken.None);

        result.StatusCode.Should().Be(status);
        _context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }
}
