using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.ConsigneeNotifications;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ConsigneeNotifications;

public sealed class GetConsigneeNotificationHandlerTests
{
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly GetConsigneeNotificationHandler _handler;

    public GetConsigneeNotificationHandlerTests() => _handler = new(_orders);

    [Fact]
    public async Task Handle_TheOrderHasAnEmailAndACode_ReturnsBothTrimmed()
    {
        var order = OrderWith(" inkoop@dezwaan.example ");
        Given(order);

        var result = await _handler.Handle(new GetConsigneeNotificationQuery("TO-0000007"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransportOrderNumber.Should().Be("TO-0000007");
        result.Value.Email.Should().Be("inkoop@dezwaan.example");
        result.Value.TrackingCode.Should().Be(order.TrackingCode!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an email")]
    public async Task Handle_TheOrderHasNoUsableEmail_IsNotFound_BecauseThereIsNothingToSend(string? email)
    {
        Given(OrderWith(email));

        var result = await _handler.Handle(new GetConsigneeNotificationQuery("TO-0000007"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_TheOrderHasNoTrackingCode_IsNotFound()
    {
        var order = OrderWith("inkoop@dezwaan.example");
        order.GetType().GetProperty(nameof(TransportOrder.TrackingCode))!.SetValue(order, null);
        Given(order);

        var result = await _handler.Handle(new GetConsigneeNotificationQuery("TO-0000007"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("not a number")]
    public async Task Handle_UnknownOrMalformedNumber_IsNotFound(string number)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await _handler.Handle(new GetConsigneeNotificationQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public void Content_NeverPrintsTheEmailOrTheCode()
    {
        var content = new ConsigneeNotificationContent("TO-0000007", "inkoop@dezwaan.example", "ABCDEFGH23456789");

        content.ToString().Should().NotContain("inkoop").And.NotContain("ABCDEFGH");
    }

    private void Given(TransportOrder order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private static TransportOrder OrderWith(string? email)
    {
        var canonical = Samples.CanonicalTransportOrder();
        canonical = canonical with { Delivery = canonical.Delivery! with { Consignee = new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", email) } };
        return TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), canonical.ToDetails(), TransportOrderNumber.FromSequence(7), IntakeChannel.ShipperApi, new CorrelationId("corr-1"), Samples.Now);
    }
}
