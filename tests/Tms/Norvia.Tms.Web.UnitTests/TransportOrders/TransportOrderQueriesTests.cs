using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.Carriers.ListActive;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ListDrivers;
using Norvia.Tms.Application.Fleet.ListVehicles;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.ListOpenDays;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderQueriesTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ServiceProvider _services;
    private readonly TransportOrderQueries _queries;

    public TransportOrderQueriesTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        _queries = new TransportOrderQueries(_services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task ListWaitingForPlanningAsync_ListsTheTransportOrdersToPlan()
    {
        IReadOnlyList<TransportOrderToPlanSummary> toPlan = [Samples.ToPlan()];
        _mediator.Send(new ListTransportOrdersToPlanQuery(), Arg.Any<CancellationToken>()).Returns(toPlan);

        var orders = await _queries.ListWaitingForPlanningAsync(Xunit.TestContext.Current.CancellationToken);

        orders.Should().BeSameAs(toPlan);
    }

    [Fact]
    public async Task ListActiveCarriersAsync_ListsTheActiveCarriers()
    {
        IReadOnlyList<CarrierView> active = [Samples.Kessler, Samples.VanDijk];
        _mediator.Send(new ListActiveCarriersQuery(), Arg.Any<CancellationToken>()).Returns(active);

        var carriers = await _queries.ListActiveCarriersAsync(Xunit.TestContext.Current.CancellationToken);

        carriers.Should().BeSameAs(active);
    }

    [Fact]
    public async Task ListVehiclesAsync_ListsTheOwnFleetsVehicles()
    {
        IReadOnlyList<VehicleView> vehicles = [];
        _mediator.Send(new ListVehiclesQuery(), Arg.Any<CancellationToken>()).Returns(vehicles);

        var listed = await _queries.ListVehiclesAsync(Xunit.TestContext.Current.CancellationToken);

        listed.Should().BeSameAs(vehicles);
    }

    [Fact]
    public async Task ListDriversAsync_ListsTheOwnFleetsDrivers()
    {
        IReadOnlyList<DriverView> drivers = [];
        _mediator.Send(new ListDriversQuery(), Arg.Any<CancellationToken>()).Returns(drivers);

        var listed = await _queries.ListDriversAsync(Xunit.TestContext.Current.CancellationToken);

        listed.Should().BeSameAs(drivers);
    }

    [Fact]
    public async Task ListPlanningDaysAsync_ListsTheOpenDaysOfThePlanningHorizon()
    {
        IReadOnlyList<PlanningDayView> days = [];
        _mediator.Send(new ListOpenPlanningDaysQuery(), Arg.Any<CancellationToken>()).Returns(days);

        var listed = await _queries.ListPlanningDaysAsync(Xunit.TestContext.Current.CancellationToken);

        listed.Should().BeSameAs(days);
    }

    [Fact]
    public async Task ListShipmentsInProgressAsync_ListsTheShipmentsInProgress()
    {
        IReadOnlyList<ShipmentInProgressSummary> inProgress = [];
        _mediator.Send(new ListShipmentsInProgressQuery(), Arg.Any<CancellationToken>()).Returns(inProgress);

        var shipments = await _queries.ListShipmentsInProgressAsync(Xunit.TestContext.Current.CancellationToken);

        shipments.Should().BeSameAs(inProgress);
    }

    [Fact]
    public async Task ListDeliveredShipmentsWithoutProofOfDeliveryAsync_ReturnsWhatTheQueryGives()
    {
        IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary> delivered = [];
        _mediator.Send(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), Arg.Any<CancellationToken>()).Returns(delivered);

        var shipments = await _queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Xunit.TestContext.Current.CancellationToken);

        shipments.Should().BeSameAs(delivered);
    }

    [Fact]
    public async Task ListOpenExceptionsAsync_ReturnsWhatTheQueryGives()
    {
        IReadOnlyList<OpenExceptionSummary> open = [];
        _mediator.Send(new ListOpenExceptionsQuery(), Arg.Any<CancellationToken>()).Returns(open);

        var exceptions = await _queries.ListOpenExceptionsAsync(Xunit.TestContext.Current.CancellationToken);

        exceptions.Should().BeSameAs(open);
    }

    [Fact]
    public async Task FindShipperNotificationTemplateAsync_AKnownException_ReturnsItsMessage()
    {
        _mediator.Send(new ShipperNotificationTemplateQuery("EX-0000005"), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ShipperNotificationTemplateResult("EX-0000005", "Your delivery failed.")));

        var message = await _queries.FindShipperNotificationTemplateAsync("EX-0000005", Xunit.TestContext.Current.CancellationToken);

        message.Should().Be("Your delivery failed.");
    }

    [Fact]
    public async Task FindShipperNotificationTemplateAsync_AnUnknownException_ReturnsNull()
    {
        _mediator.Send(new ShipperNotificationTemplateQuery("EX-0000999"), Arg.Any<CancellationToken>())
            .Returns(Result<ShipperNotificationTemplateResult>.Invalid(new ValidationError("Exception", "There is no Exception EX-0000999.", "ExceptionNotFound", ValidationSeverity.Error)));

        var message = await _queries.FindShipperNotificationTemplateAsync("EX-0000999", Xunit.TestContext.Current.CancellationToken);

        message.Should().BeNull();
    }

    [Fact]
    public async Task FindShipperNotificationTemplateAsync_AnyOtherFailure_Throws()
    {
        _mediator.Send(new ShipperNotificationTemplateQuery("EX-0000005"), Arg.Any<CancellationToken>()).Returns(Result<ShipperNotificationTemplateResult>.Error("boom"));

        var find = () => _queries.FindShipperNotificationTemplateAsync("EX-0000005", Xunit.TestContext.Current.CancellationToken);

        await find.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FindTrackingTimelineAsync_AKnownShipment_ReturnsItsTimeline()
    {
        var timeline = new ShipmentTimeline([], [], [], []);
        _mediator.Send(new GetTrackingTimelineQuery("SH-0000012"), Arg.Any<CancellationToken>()).Returns(Result.Success(timeline));

        var found = await _queries.FindTrackingTimelineAsync("SH-0000012", Xunit.TestContext.Current.CancellationToken);

        found.Should().BeSameAs(timeline);
    }

    [Fact]
    public async Task FindTrackingTimelineAsync_AnUnknownShipment_ReturnsNull()
    {
        _mediator.Send(new GetTrackingTimelineQuery("SH-0000999"), Arg.Any<CancellationToken>()).Returns(Result<ShipmentTimeline>.NotFound());

        var found = await _queries.FindTrackingTimelineAsync("SH-0000999", Xunit.TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindShipmentAsync_AKnownNumber_ReturnsTheShipment()
    {
        var view = Samples.Shipment("SH-0000012");
        _mediator.Send(new GetShipmentQuery("SH-0000012"), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var shipment = await _queries.FindShipmentAsync("SH-0000012", Xunit.TestContext.Current.CancellationToken);

        shipment.Should().BeSameAs(view);
    }

    [Fact]
    public async Task FindShipmentAsync_AnUnknownNumber_ReturnsNull()
    {
        _mediator.Send(new GetShipmentQuery("SH-0000999"), Arg.Any<CancellationToken>()).Returns(Result<ShipmentView>.NotFound());

        var shipment = await _queries.FindShipmentAsync("SH-0000999", Xunit.TestContext.Current.CancellationToken);

        shipment.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_AKnownNumber_ReturnsTheTransportOrder()
    {
        var view = Samples.View("TO-0000042");
        _mediator.Send(new GetTransportOrderQuery("TO-0000042"), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var order = await _queries.FindAsync("TO-0000042", Xunit.TestContext.Current.CancellationToken);

        order.Should().BeSameAs(view);
    }

    [Fact]
    public async Task FindAsync_AnUnknownNumber_ReturnsNull()
    {
        _mediator.Send(new GetTransportOrderQuery("TO-0000999"), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.NotFound());

        var order = await _queries.FindAsync("TO-0000999", Xunit.TestContext.Current.CancellationToken);

        order.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryFails_ThrowsRatherThanSayingThereIsNone()
    {
        _mediator.Send(new GetTransportOrderQuery("TO-0000042"), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.Error("The query failed."));

        var find = () => _queries.FindAsync("TO-0000042", Xunit.TestContext.Current.CancellationToken);

        await find.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListRejectedAsync_ListsTheRejectedBookings()
    {
        IReadOnlyList<TransportOrderSummary> rejected = [Samples.RejectedSummary()];
        _mediator.Send(new ListTransportOrdersQuery(TransportOrderStatus.Rejected), Arg.Any<CancellationToken>()).Returns(rejected);

        var bookings = await _queries.ListRejectedAsync(Xunit.TestContext.Current.CancellationToken);

        bookings.Should().BeSameAs(rejected);
    }

    [Fact]
    public async Task FindRejectedAsync_ARejectedBooking_ReturnsIt()
    {
        var view = Samples.RejectedView();
        _mediator.Send(new GetRejectedBookingQuery(view.Id), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var booking = await _queries.FindRejectedAsync(view.Id, Xunit.TestContext.Current.CancellationToken);

        booking.Should().BeSameAs(view);
    }

    [Fact]
    public async Task FindRejectedAsync_AnUnknownId_ReturnsNull()
    {
        var id = Guid.NewGuid();
        _mediator.Send(new GetRejectedBookingQuery(id), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.NotFound());

        var booking = await _queries.FindRejectedAsync(id, Xunit.TestContext.Current.CancellationToken);

        booking.Should().BeNull();
    }

    [Fact]
    public async Task FindRejectedAsync_WhenTheQueryFails_ThrowsRatherThanSayingThereIsNone()
    {
        var id = Guid.NewGuid();
        _mediator.Send(new GetRejectedBookingQuery(id), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.Error("The query failed."));

        var find = () => _queries.FindRejectedAsync(id, Xunit.TestContext.Current.CancellationToken);

        await find.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose() => _services.Dispose();
}
