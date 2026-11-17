using Ardalis.Specification;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderByTrackingCodeSpecTests
{
    [Fact]
    public void Evaluate_MatchesTheOneTransportOrderTheCodeOpens_AndNotAReplacedOne()
    {
        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(1), IntakeChannel.ShipperApi, new CorrelationId("c"), DateTimeOffset.UnixEpoch);
        var other = TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(2), IntakeChannel.ShipperApi, new CorrelationId("c"), DateTimeOffset.UnixEpoch);
        var oldCode = order.TrackingCode!;
        order.ReplaceTrackingCode();

        new TransportOrderByTrackingCodeSpec(order.TrackingCode!).Evaluate(new[] { order, other }).Should().Equal(order);
        new TransportOrderByTrackingCodeSpec(oldCode).Evaluate(new[] { order, other }).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ARejectedBooking_IsNeverMatched()
    {
        var rejected = TransportOrder.Reject(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), [new(RejectionCode.GoodsLinesMissing, "None.")], IntakeChannel.ShipperApi, new CorrelationId("c"), DateTimeOffset.UnixEpoch);

        new TransportOrderByTrackingCodeSpec(TrackingCode.Generate()).Evaluate(new[] { rejected }).Should().BeEmpty();
    }
}
