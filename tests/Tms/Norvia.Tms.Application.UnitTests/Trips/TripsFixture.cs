using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// Today is Monday 12 October 2026 with fifteen open days, as in <see cref="PlanningEditsFixture"/>. Anouk and Joost hold a
/// licence for the van only, so the truck gives LicenceNotHeld.
/// </summary>
internal sealed class TripsFixture
{
    public static readonly DateOnly Tuesday = new(2026, 10, 13);

    private long _sequence;

    public TripsFixture()
    {
        Planning.OpenDays();
        Trips = InMemoryRepository.Over(Stored);
        Numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(TripNumber.FromSequence(++_sequence)));
        var plannedDriving = PlannedDriving;

        // What the infrastructure does: the planned driving of every stored Trip, so a Draft uses up hours and discarding it gives them back.
        plannedDriving.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var (first, last) = (call.ArgAt<DateOnly>(0), call.ArgAt<DateOnly>(1));
            IReadOnlyList<PlannedDriving> planned = [.. Stored.Select(trip => trip.PlannedDriving).OfType<PlannedDriving>().Where(driving => driving.Day >= first && driving.Day <= last)];
            return Task.FromResult(planned);
        });
        var rules = new TripAvailabilityRules(Planning.Days.AsRead(), Planning.Vehicles, Planning.Drivers, plannedDriving, DrivingHourLimits.Default, Planning.Horizon, Planning.Time);
        NeedsCheck = new TripNeedsCheck(rules);
        Checks = new TripPlanningChecks(Trips.AsRead(), Planning.Days.AsRead(), rules);
        Shipments = InMemoryRepository.Over(StoredShipments);
        TransportOrders = InMemoryRepository.Over(StoredOrders);
        TripShipments = new TripShipments(Shipments.AsRead(), TransportOrders.AsRead());
        Amendments = new TripAmendments(Shipments, TripShipments, Planning.Vehicles, Planning.Drivers, Planning.Time);
    }

    public List<Shipment> StoredShipments { get; } = [];

    public List<TransportOrder> StoredOrders { get; } = [];

    public IRepository<Shipment> Shipments { get; }

    public IRepository<TransportOrder> TransportOrders { get; }

    public TripShipments TripShipments { get; }

    public TripAmendments Amendments { get; }

    /// <summary>What the infrastructure reports: the planned driving of every stored Trip.</summary>
    public IPlannedDrivingHours PlannedDriving { get; } = Substitute.For<IPlannedDrivingHours>();

    public PlanningEditsFixture Planning { get; } = new();

    public List<Trip> Stored { get; } = [];

    public IRepository<Trip> Trips { get; }

    public ITripNumbers Numbers { get; } = Substitute.For<ITripNumbers>();

    public TripPlanningChecks Checks { get; }

    public TripNeedsCheck NeedsCheck { get; }

    public Trip Existing(DateOnly day, VehicleId? vehicle, DriverId? driver)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(100 + Stored.Count), day, vehicle, driver, new IdempotencyKey(Guid.NewGuid().ToString()));
        Stored.Add(trip);
        return trip;
    }

    /// <summary>
    /// A Trip of <see cref="Tuesday"/> with one Released Shipment, Dispatched at version 1 under "corr-dispatch": the Van and Anouk. Its domain
    /// events are cleared, so what a test sees is what it did.
    /// </summary>
    public async Task<Trip> DispatchedAsync()
    {
        var trip = Existing(Tuesday, PlanningEditsFixture.Van, PlanningEditsFixture.Anouk);
        var shipment = ReleasedShipment(Tuesday);
        trip.Plan(new TimeOnly(9, 0), TimeSpan.FromHours(4));
        trip.AddShipment(shipment.Id);
        var onTrip = await TripShipments.OfAsync([shipment.Id], CancellationToken.None);
        var vehicle = Planning.Vehicles.ListActive().Single(candidate => candidate.Id == PlanningEditsFixture.Van);
        var driver = Planning.Drivers.ListActive().Single(candidate => candidate.Id == PlanningEditsFixture.Anouk);
        trip.Dispatch(new CorrelationId("corr-dispatch"), Planning.Time.GetUtcNow(), vehicle, driver, [.. onTrip.Select(item => new DispatchedShipment(item.Shipment, item.Details))]);
        trip.ClearDomainEvents();
        return trip;
    }

    /// <summary>
    /// A Released Own fleet Shipment that collects on <paramref name="collectionDay"/> (08:00-12:00 in Amsterdam) and delivers on <paramref name="deliveryDay"/>
    /// (default the same day), carrying <paramref name="pallets"/> pallets of <paramref name="weightKg"/> kg in all.
    /// </summary>
    public Shipment ReleasedShipment(DateOnly collectionDay, int pallets = 2, decimal weightKg = 500m, DateOnly? deliveryDay = null, bool release = true, CarriedBy? carriedBy = null)
    {
        var sequence = ++_shipmentSequence;
        var delivery = deliveryDay ?? collectionDay;
        var details = new TransportOrderDetails(
            $"REF-{sequence}",
            new CollectionDetails(new Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"), new TimeWindow(At(collectionDay, 8), At(collectionDay, 12))),
            new DeliveryDetails(new Address("Oudegracht 112", "3511 AW", "Utrecht", "NL"), new Consignee("Café De Zwaan", null, null, null), new TimeWindow(At(delivery, 13), At(delivery, 17))),
            [new GoodsLine("pallet", pallets, weightKg, null)],
            null);
        var order = TransportOrder.Accept(new ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("corr"), Planning.Time.GetUtcNow());
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), carriedBy ?? CarriedBy.OwnFleet, new IdempotencyKey($"plan-{sequence}"), new CorrelationId("corr"), Planning.Time.GetUtcNow())!;
        if (release)
        {
            shipment.Release(order, new IdempotencyKey($"release-{sequence}"), new CorrelationId("corr"), Planning.Time.GetUtcNow(), carriedBy is { IsOwnFleet: false } ? CarrierBookingNumber.FromSequence(sequence) : null);
        }

        shipment.ClearDomainEvents();
        StoredOrders.Add(order);
        StoredShipments.Add(shipment);
        return shipment;
    }

    private long _shipmentSequence;

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 0)), TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam").GetUtcOffset(day.ToDateTime(new TimeOnly(hour, 0))));
}
