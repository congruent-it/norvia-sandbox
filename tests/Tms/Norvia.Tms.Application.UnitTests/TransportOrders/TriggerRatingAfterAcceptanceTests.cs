using Ardalis.Result;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class TriggerRatingAfterAcceptanceTests
{
    private readonly IRatingTrigger _trigger = Substitute.For<IRatingTrigger>();
    private readonly SubmitTransportOrderCommand _command = new(Samples.CanonicalTransportOrder(), IntakeChannel.ShipperApi, "corr-1");

    [Fact]
    public async Task Handle_Accepted_TriggersRatingOnceTheSubmissionIsDone()
    {
        var triggeredBeforeCommit = true;
        var result = await new TriggerRatingAfterAcceptance(_trigger).Handle(_command, (_, _) =>
        {
            triggeredBeforeCommit = _trigger.ReceivedCalls().Any();
            return ValueTask.FromResult(Result.Success(Outcome(TransportOrderStatus.Accepted, "TO-0000042")));
        }, CancellationToken.None);

        result.Value.TransportOrderNumber.Should().Be("TO-0000042");
        triggeredBeforeCommit.Should().BeFalse("the order must be committed before the worker looks for it");
        _trigger.Received(1).Trigger();
    }

    [Fact]
    public async Task Handle_Rejected_DoesNotTriggerRating()
    {
        await new TriggerRatingAfterAcceptance(_trigger).Handle(
            _command,
            (_, _) => ValueTask.FromResult(Result.Success(Outcome(TransportOrderStatus.Rejected, null))),
            CancellationToken.None);

        _trigger.DidNotReceive().Trigger();
    }

    private static SubmitTransportOrderResult Outcome(TransportOrderStatus status, string? number) =>
        new(Guid.NewGuid(), status, number, Array.Empty<RejectionReasonView>());
}
