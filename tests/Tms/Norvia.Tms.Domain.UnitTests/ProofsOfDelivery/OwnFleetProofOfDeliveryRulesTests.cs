using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.ProofsOfDelivery;

/// <summary>The Driver report rules of P12 for a Proof of delivery from Own fleet, in the order the TMS checks them.</summary>
public sealed class OwnFleetProofOfDeliveryRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DriverId Joost = new("DRV-0001");
    private static readonly TrackingLocation Place = new("Utrecht", "3511 AW", "NL");

    private readonly ProofOfDeliveryRules _rules = new(TimeSpan.FromMinutes(5));
    private readonly Shipment _shipment = ReleasedOwnFleetShipment(1);
    private readonly Trip _trip;

    public OwnFleetProofOfDeliveryRulesTests()
    {
        _trip = Trip.CreateDraft(TripNumber.FromSequence(1), new DateOnly(2026, 10, 13), new VehicleId("VEH-0002"), Joost, new IdempotencyKey("k"));
        _trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        _trip.AddShipment(_shipment.Id);
    }

    [Fact]
    public void AProofOfDelivery_AfterTheCollection_OnATripUnderWay_BreaksNoRule()
    {
        Start();

        Check(Report(), Collected()).Should().BeNull();
    }

    [Fact]
    public void AnotherDriversTrip_AndNoTrip_AreTripNotFound()
    {
        Start();

        Check(Report(driver: new DriverId("DRV-0099")), Collected())!.Code.Should().Be(ProofOfDeliveryRefusalCode.TripNotFound);
        _rules.CheckOwnFleetReport(null, _shipment, Report(), StopOutcomes.From([]), Now)!.Code.Should().Be(ProofOfDeliveryRefusalCode.TripNotFound);
    }

    [Fact]
    public void ATripThatIsNotInProgress_IsTripNotUnderWay()
    {
        Check(Report(), Collected())!.Code.Should().Be(ProofOfDeliveryRefusalCode.TripNotUnderWay);
    }

    [Fact]
    public void AShipmentNotOnTheTrip_IsShipmentNotOnTrip_WhateverVersionTheReportNames()
    {
        Start();
        var other = ReleasedOwnFleetShipment(2);

        _rules.CheckOwnFleetReport(_trip, other, Report(shipment: other, tripVersion: 0), StopOutcomes.From([]), Now)!.Code.Should().Be(ProofOfDeliveryRefusalCode.ShipmentNotOnTrip);
        _rules.CheckOwnFleetReport(_trip, null, Report(), StopOutcomes.From([]), Now)!.Code.Should().Be(ProofOfDeliveryRefusalCode.ShipmentNotOnTrip);
        Check(Report(tripVersion: 0), Collected()).Should().BeNull("the Shipment is on the latest version, which is all that counts");
    }

    [Fact]
    public void AProofOfDeliveryBeforeTheCollection_IsCollectionBeforeDelivery_AlsoWhenTheCollectionFailed()
    {
        Start();

        Check(Report())!.Code.Should().Be(ProofOfDeliveryRefusalCode.CollectionBeforeDelivery);
        Check(Report(), CollectionFailed())!.Code.Should().Be(ProofOfDeliveryRefusalCode.CollectionBeforeDelivery);
    }

    [Fact]
    public void ADeliveryTimeBeyondTheClockSkew_IsDeliveryTimeInTheFuture()
    {
        Start();

        Check(Report(deliveryTime: Now.AddMinutes(5)), Collected()).Should().BeNull();
        Check(Report(deliveryTime: Now.AddMinutes(6)), Collected())!.Code.Should().Be(ProofOfDeliveryRefusalCode.DeliveryTimeInTheFuture);
    }

    [Fact]
    public void ARuleThatComesFirst_IsTheOneReported()
    {
        Check(Report(driver: new DriverId("DRV-0099"), deliveryTime: Now.AddDays(1)))!.Code.Should().Be(ProofOfDeliveryRefusalCode.TripNotFound);
    }

    private ProofOfDeliveryRefusal? Check(ProofOfDeliveryReport report, params TrackingEvent[] recorded) =>
        _rules.CheckOwnFleetReport(_trip, _shipment, report, StopOutcomes.From(recorded), Now);

    private void Start()
    {
        _trip.Dispatch(new CorrelationId("d"), Now, new Vehicle(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true), new Driver(Joost, "Joost", new HashSet<LicenceCategory>(), true), []);
        _trip.Start(new CorrelationId("s"), Now);
    }

    private ProofOfDeliveryReport Report(DriverId? driver = null, Shipment? shipment = null, int tripVersion = 1, DateTimeOffset? deliveryTime = null) =>
        new(
            ProofOfDeliverySource.OwnFleet, "doc-1", null, "J. Becker", deliveryTime ?? Now.AddMinutes(-5), Place, DeliveryCondition.Clean, null,
            [new ProofOfDeliveryFile(Guid.NewGuid(), "signature.png", "image/png", 4)],
            OwnFleet: new OwnFleetReport(_trip.Number, driver ?? Joost, (shipment ?? _shipment).Number, tripVersion));

    private TrackingEvent Collected() => Recorded(TrackingEventType.Collected);

    private TrackingEvent CollectionFailed() => Recorded(TrackingEventType.CollectionFailed);

    private TrackingEvent Recorded(TrackingEventType type) =>
        _shipment.Record(
            new TrackingReport(
                TrackingSource.OwnFleet, Guid.NewGuid().ToString(), null, null, type, Now.AddMinutes(-30), Place, null, type.ToString(),
                new OwnFleetReport(_trip.Number, Joost, _shipment.Number, 1)),
            new CorrelationId("r"),
            Now);

    private static Shipment ReleasedOwnFleetShipment(long sequence)
    {
        var at = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("i"), at);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey("p"), new CorrelationId("p"), at)!;
        shipment.Release(order, new IdempotencyKey("r"), new CorrelationId("r"), at);
        return shipment;
    }
}
