using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderSpecsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ByNumber_Evaluated_FindsOnlyThatTransportOrder()
    {
        var orders = new[] { Accepted(1, Morning), Accepted(2, Morning), Accepted(3, Morning) };

        var found = new TransportOrderByNumberSpec(TransportOrderNumber.FromSequence(2)).Evaluate(orders);

        found.Should().ContainSingle().Which.Number.Should().Be(TransportOrderNumber.FromSequence(2));
    }

    [Fact]
    public void ByStatus_Evaluated_ListsThemInTheOrderTheyWereReceived()
    {
        var orders = new[] { Accepted(1, Morning.AddHours(2)), Accepted(2, Morning), Accepted(3, Morning.AddHours(1)) };

        var listed = new TransportOrdersByStatusSpec(TransportOrderStatus.Accepted).Evaluate(orders);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000003", "TO-0000001");
    }

    [Fact]
    public void ById_Evaluated_FindsOnlyThatTransportOrder()
    {
        var rejected = Rejected(Morning);
        var orders = new[] { Accepted(1, Morning), rejected, Rejected(Morning) };

        var found = new TransportOrderByIdSpec(rejected.Id).Evaluate(orders);

        found.Should().ContainSingle().Which.Should().BeSameAs(rejected);
    }

    [Fact]
    public void ByStatus_Rejected_ListsOnlyTheRejectedBookings()
    {
        var rejected = Rejected(Morning);
        var orders = new[] { Accepted(1, Morning), rejected };

        var listed = new TransportOrdersByStatusSpec(TransportOrderStatus.Rejected).Evaluate(orders);

        listed.Should().ContainSingle().Which.Should().BeSameAs(rejected);
    }

    [Fact]
    public void ByShipperReference_Evaluated_FindsTheShippersAcceptedTransportOrderWithThatReference()
    {
        var held = Accepted(1, Morning);
        var orders = new[]
        {
            Rejected(Morning),
            held,
            TransportOrder.Accept(new ShipperId("SHP-0003"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(2), new CorrelationId("corr-2"), Morning),
            TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails() with { ShipperReference = "BD-2026-0002" }, TransportOrderNumber.FromSequence(3), new CorrelationId("corr-3"), Morning),
        };

        var found = new TransportOrderByShipperReferenceSpec(new ShipperId("SHP-0001"), "BD-2026-0001").Evaluate(orders);

        found.Should().ContainSingle().Which.Should().BeSameAs(held);
    }

    private static TransportOrder Rejected(DateTimeOffset receivedAt) => TransportOrder.Reject(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        [new RejectionReason(RejectionCode.InactiveShipper, "Inactive.")],
        new CorrelationId("corr-rejected"),
        receivedAt);

    private static TransportOrder Accepted(long sequence, DateTimeOffset receivedAt) => TransportOrder.Accept(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(sequence),
        new CorrelationId($"corr-{sequence}"),
        receivedAt);
}
