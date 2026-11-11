using System.Text.Json;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ShipperTracking;
using NSubstitute;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The notification agent's template read holds the template's words and nothing else (ADR 0011, #444).</summary>
public sealed class PlannerNotificationAgentViewTests
{
    [Fact]
    public void TheTemplateView_IsOnlyTheMessage()
    {
        typeof(PlannerNotificationTemplateView).GetProperties().Select(property => property.Name).Should().Equal("Message");
    }

    [Fact]
    public void TheTemplateView_CarriesNoExceptionNumberCarrierOrRemark()
    {
        var json = JsonSerializer.Serialize(new PlannerNotificationTemplateView("The delivery of your shipment BD-1 didn't succeed."));

        json.Should().NotContainAny("EX-", "exception", "Exception", "carrier", "Carrier", "remark");
    }

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public async Task GetTemplate_AnExceptionThatExists_IsTheTemplatesWordsOnly()
    {
        _mediator.Send(new ShipperNotificationTemplateQuery("EX-0000005"), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ShipperNotificationTemplateResult("EX-0000005", "Your delivery failed.")));

        var result = await PlannerNotificationAgentEndpoints.GetTemplateAsync("EX-0000005", _mediator, TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<Ok<PlannerNotificationTemplateView>>().Which.Value.Should().Be(new PlannerNotificationTemplateView("Your delivery failed."));
    }

    [Fact]
    public async Task GetTemplate_AnUnknownException_Is404()
    {
        _mediator.Send(new ShipperNotificationTemplateQuery("EX-9999999"), Arg.Any<CancellationToken>()).Returns(Result<ShipperNotificationTemplateResult>.NotFound());

        var result = await PlannerNotificationAgentEndpoints.GetTemplateAsync("EX-9999999", _mediator, TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetShipperView_AnOrderThatExists_IsTheSameViewAsTheReplyAgentsAtTheTimeOfTheAnswer()
    {
        var now = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        var detail = new ShipperTransportOrderDetail(
            "TO-0000042", "BD-1", new ShipperPlaceView("Rotterdam", "NL"), new ShipperPlaceView("Utrecht", "NL"), null, null, TrackingStatus.InTransit, null, null, [], null);
        _mediator.Send(new GetShipperViewQuery("TO-0000042"), Arg.Any<CancellationToken>()).Returns(Result.Success(new ShipperView(detail, [])));

        var result = await PlannerNotificationAgentEndpoints.GetShipperViewAsync("TO-0000042", _mediator, new FakeTimeProvider(now), TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<Ok<ReplyAgentTrackingView>>().Which.Value!.AsOf.Should().Be(now);
    }

    [Fact]
    public async Task GetShipperView_AnUnknownOrder_Is404()
    {
        _mediator.Send(new GetShipperViewQuery("TO-9999999"), Arg.Any<CancellationToken>()).Returns(Result<ShipperView>.NotFound());

        var result = await PlannerNotificationAgentEndpoints.GetShipperViewAsync("TO-9999999", _mediator, TimeProvider.System, TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }
}
