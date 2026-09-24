using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Accept_Booking_IsAcceptedWithItsNumber()
    {
        var number = TransportOrderNumber.FromSequence(7);

        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), SampleDetails(), number, new CorrelationId("corr-1"), ReceivedAt);

        order.Status.Should().Be(TransportOrderStatus.Accepted);
        order.Number.Should().Be(number);
    }

    [Fact]
    public void Accept_Booking_KeepsWhatTheShipperSubmitted()
    {
        var details = SampleDetails();

        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(7), new CorrelationId("corr-1"), ReceivedAt);

        order.ShipperId.Should().Be(new ShipperId("SHP-0001"));
        order.Details.Should().Be(details);
        order.CorrelationId.Should().Be(new CorrelationId("corr-1"));
        order.ReceivedAt.Should().Be(ReceivedAt);
    }

    [Fact]
    public void Accept_Booking_RaisesTransportOrderAccepted()
    {
        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), SampleDetails(), TransportOrderNumber.FromSequence(7), new CorrelationId("corr-1"), ReceivedAt);

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransportOrderAcceptedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public void Reject_Booking_IsRejectedWithItsReasonsAndNoNumber()
    {
        RejectionReason[] reasons = [new(RejectionCode.InactiveShipper, "Inactive."), new(RejectionCode.GoodsLinesMissing, "No goods.")];

        var order = TransportOrder.Reject(new ShipperId("SHP-0001"), SampleDetails(), reasons, new CorrelationId("corr-1"), ReceivedAt);

        order.Status.Should().Be(TransportOrderStatus.Rejected);
        order.Number.Should().BeNull();
        order.RejectionReasons.Should().Equal(reasons);
    }

    [Fact]
    public void Reject_Booking_KeepsWhatTheShipperSubmitted()
    {
        var details = SampleDetails();

        var order = TransportOrder.Reject(new ShipperId("SHP-0001"), details, [Inactive], new CorrelationId("corr-1"), ReceivedAt);

        order.ShipperId.Should().Be(new ShipperId("SHP-0001"));
        order.Details.Should().Be(details);
        order.CorrelationId.Should().Be(new CorrelationId("corr-1"));
        order.ReceivedAt.Should().Be(ReceivedAt);
    }

    [Fact]
    public void Reject_Booking_RaisesTransportOrderRejected()
    {
        var order = TransportOrder.Reject(new ShipperId("SHP-0001"), SampleDetails(), [Inactive], new CorrelationId("corr-1"), ReceivedAt);

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransportOrderRejectedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public void Reject_WithoutReasons_Throws()
    {
        var act = () => TransportOrder.Reject(new ShipperId("SHP-0001"), SampleDetails(), [], new CorrelationId("corr-1"), ReceivedAt);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accept_Booking_HasNoRejectionReasons()
    {
        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), SampleDetails(), TransportOrderNumber.FromSequence(7), new CorrelationId("corr-1"), ReceivedAt);

        order.RejectionReasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CorrelationId_Blank_Throws(string value)
    {
        var act = () => new CorrelationId(value);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShipperId_Blank_Throws(string value)
    {
        var act = () => new ShipperId(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Details_SameContent_AreEqual()
    {
        SampleDetails().Should().Be(SampleDetails());
    }

    [Fact]
    public void Details_DifferentGoodsLines_AreNotEqual()
    {
        var other = SampleDetails() with { GoodsLines = [new GoodsLine("parcel", 1, 2m, null)] };

        other.Should().NotBe(SampleDetails());
    }

    private static readonly RejectionReason Inactive = new(RejectionCode.InactiveShipper, "Inactive.");

    internal static TransportOrderDetails SampleDetails() => new(
        ShipperReference: "BD-2026-0001",
        Collection: new CollectionDetails(
            new Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
            new TimeWindow(ReceivedAt.AddDays(1), ReceivedAt.AddDays(1).AddHours(4))),
        Delivery: new DeliveryDetails(
            new Address("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
            new Consignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", null),
            null),
        GoodsLines: [new GoodsLine("pallet", 2, 850m, new Dimensions(120m, 80m, 150m))],
        Instructions: "Ring at the side entrance");
}
