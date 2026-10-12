using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TrackingEvents;

/// <summary>The Driver report rules of P12 for a Tracking event from Own fleet, in the order the TMS checks them.</summary>
public sealed class OwnFleetReportRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DriverId Joost = new("DRV-0001");
    private static readonly TrackingLocation Place = new("Utrecht", "3511 AW", "NL");

    private readonly TrackingEventRules _rules = new(TimeSpan.FromMinutes(5));
    private readonly Shipment _shipment = ReleasedOwnFleetShipment(1);
    private readonly Trip _trip;

    public OwnFleetReportRulesTests()
    {
        _trip = Trip.CreateDraft(TripNumber.FromSequence(1), new DateOnly(2026, 10, 13), new VehicleId("VEH-0002"), Joost, new IdempotencyKey("k"));
        _trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        _trip.AddShipment(_shipment.Id);
    }

    [Fact]
    public void ACollectedReport_OnATripUnderWay_BreaksNoRule()
    {
        Start();

        Check(Report(TrackingEventType.Collected)).Should().BeNull();
    }

    [Fact]
    public void AnotherDriversTrip_AndNoTrip_AreTripNotFound()
    {
        Start();

        Check(Report(TrackingEventType.Collected, driver: new DriverId("DRV-0099")))!.Code.Should().Be(TrackingEventRefusalCode.TripNotFound);
        _rules.CheckOwnFleetReport(null, _shipment, Report(TrackingEventType.Collected), StopOutcomes.From([]), Now)!.Code.Should().Be(TrackingEventRefusalCode.TripNotFound);
    }

    [Fact]
    public void ATripThatIsNotInProgress_IsTripNotUnderWay()
    {
        Check(Report(TrackingEventType.Collected))!.Code.Should().Be(TrackingEventRefusalCode.TripNotUnderWay, "a Draft was never started");

        _trip.Dispatch(new CorrelationId("d"), Now, new Vehicle(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true), new Driver(Joost, "Joost", new HashSet<LicenceCategory>(), true), []);
        Check(Report(TrackingEventType.Collected))!.Code.Should().Be(TrackingEventRefusalCode.TripNotUnderWay, "a Dispatched Trip was not started");
    }

    [Fact]
    public void AShipmentNotOnTheTrip_IsShipmentNotOnTrip_WhateverVersionTheReportNames()
    {
        Start();
        var other = ReleasedOwnFleetShipment(2);

        var refusal = _rules.CheckOwnFleetReport(_trip, other, Report(TrackingEventType.Collected, shipment: other, tripVersion: 0), StopOutcomes.From([]), Now);
        refusal!.Code.Should().Be(TrackingEventRefusalCode.ShipmentNotOnTrip);
        _rules.CheckOwnFleetReport(_trip, null, Report(TrackingEventType.Collected), StopOutcomes.From([]), Now)!.Code.Should().Be(TrackingEventRefusalCode.ShipmentNotOnTrip);
        Check(Report(TrackingEventType.Collected, tripVersion: 0)).Should().BeNull("the Shipment is on the latest version, which is all that counts");
    }

    [Theory]
    [InlineData(TrackingEventType.Collected, TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Collected, TrackingEventType.CollectionFailed)]
    [InlineData(TrackingEventType.CollectionFailed, TrackingEventType.Collected)]
    public void ASecondOutcomeForTheCollectionStop_IsStopAlreadyCompleted(TrackingEventType first, TrackingEventType second)
    {
        Start();

        Check(Report(second), Recorded(first))!.Code.Should().Be(TrackingEventRefusalCode.StopAlreadyCompleted);
    }

    [Theory]
    [InlineData(TrackingEventType.Delivered)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    public void ASecondOutcomeForTheDeliveryStop_IsStopAlreadyCompleted(TrackingEventType second)
    {
        Start();

        Check(Report(second), Recorded(TrackingEventType.Collected), Recorded(TrackingEventType.Delivered))!.Code.Should().Be(TrackingEventRefusalCode.StopAlreadyCompleted);
    }

    [Fact]
    public void ReportDamage_IsExemptFromStopAlreadyCompleted_AndFromTheCollectionBeforeDeliveryRule()
    {
        Start();

        Check(Report(TrackingEventType.Damaged), Recorded(TrackingEventType.Collected), Recorded(TrackingEventType.Delivered)).Should().BeNull();
        Check(Report(TrackingEventType.Damaged)).Should().BeNull("damage can be reported at any Stop, before a collection too");
    }

    [Theory]
    [InlineData(TrackingEventType.Delivered)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    public void ADeliveryReportBeforeTheCollection_IsCollectionBeforeDelivery_AlsoWhenTheCollectionFailed(TrackingEventType type)
    {
        Start();

        Check(Report(type))!.Code.Should().Be(TrackingEventRefusalCode.CollectionBeforeDelivery);
        Check(Report(type), Recorded(TrackingEventType.CollectionFailed))!.Code.Should().Be(TrackingEventRefusalCode.CollectionBeforeDelivery);
    }

    [Fact]
    public void ADeliveryReportAfterTheCollection_BreaksNoRule()
    {
        Start();

        Check(Report(TrackingEventType.Delivered), Recorded(TrackingEventType.Collected)).Should().BeNull();
    }

    [Fact]
    public void AnEventTimeBeyondTheClockSkew_IsEventTimeInTheFuture()
    {
        Start();

        Check(Report(TrackingEventType.Collected, eventTime: Now.AddMinutes(5)))!.Should().BeNull();
        Check(Report(TrackingEventType.Collected, eventTime: Now.AddMinutes(6)))!.Code.Should().Be(TrackingEventRefusalCode.EventTimeInTheFuture);
    }

    [Theory]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    [InlineData(TrackingEventType.OutForDelivery)]
    public void ATypeTheDriverAppDoesNotSend_IsUnknownEventType(TrackingEventType type)
    {
        Start();

        Check(Report(type))!.Code.Should().Be(TrackingEventRefusalCode.UnknownEventType);
    }

    [Fact]
    public void ARuleThatComesFirst_IsTheOneReported()
    {
        // Another Driver's Trip that was never started, for a Shipment that isn't on it, in the future: the Trip is not theirs.
        Check(Report(TrackingEventType.Collected, driver: new DriverId("DRV-0099"), eventTime: Now.AddDays(1)))!.Code.Should().Be(TrackingEventRefusalCode.TripNotFound);
    }

    [Fact]
    public void StopOutcomes_ReadTheOutcomeOfEachStop_WithItsTimeAndRemark_AndDamageIsNone()
    {
        var collected = Recorded(TrackingEventType.Collected, at: Now.AddHours(-3), remark: "Dock 4");
        var failed = Recorded(TrackingEventType.DeliveryFailed, at: Now.AddHours(-1), remark: "Nobody home");
        var damaged = Recorded(TrackingEventType.Damaged, at: Now.AddHours(-2), remark: "Crushed pallet");

        var outcomes = StopOutcomes.From([failed, damaged, collected]);

        outcomes.Of(_shipment.Id, StopKind.Collection).Should().Be(new StopOutcome(StopOutcomeKind.Collected, Now.AddHours(-3), "Dock 4"));
        outcomes.Of(_shipment.Id, StopKind.Delivery).Should().Be(new StopOutcome(StopOutcomeKind.DeliveryFailed, Now.AddHours(-1), "Nobody home"));
        outcomes.Of(ShipmentId.New(), StopKind.Collection).IsOpen.Should().BeTrue();
    }

    private TrackingEventRefusal? Check(TrackingReport report, params TrackingEvent[] recorded) =>
        _rules.CheckOwnFleetReport(_trip, _shipment, report, StopOutcomes.From(recorded), Now);

    private void Start()
    {
        _trip.Dispatch(new CorrelationId("d"), Now, new Vehicle(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true), new Driver(Joost, "Joost", new HashSet<LicenceCategory>(), true), []);
        _trip.Start(new CorrelationId("s"), Now);
    }

    private TrackingReport Report(TrackingEventType type, DriverId? driver = null, Shipment? shipment = null, int tripVersion = 1, DateTimeOffset? eventTime = null) =>
        new(
            TrackingSource.OwnFleet, Guid.NewGuid().ToString(), null, null, type, eventTime ?? Now.AddMinutes(-5), Place, null, type.ToString(),
            new OwnFleetReport(_trip.Number, driver ?? Joost, (shipment ?? _shipment).Number, tripVersion));

    private TrackingEvent Recorded(TrackingEventType type, DateTimeOffset? at = null, string? remark = null) =>
        _shipment.Record(Report(type, eventTime: at) with { Remark = remark }, new CorrelationId("r"), Now);

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
