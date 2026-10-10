using System.Reflection;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Domain.UnitTests.Trips;

/// <summary>The delivery state of each Trip version (P11): Sending, Delivered to the Driver app or Not delivered.</summary>
public sealed class TripDeliveryTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly DateTimeOffset At = new(2026, 10, 13, 9, 5, 0, TimeSpan.Zero);
    private static readonly CorrelationId Report = new("corr-report");
    private static readonly Vehicle Truck = new(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true);
    private static readonly Driver Joost = new(new DriverId("DRV-0001"), "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.C }, true);

    [Fact]
    public void Deliveries_OfADraft_AreNone()
    {
        Draft().Deliveries.Should().BeEmpty();
    }

    [Fact]
    public void Dispatch_StartsVersion1Sending()
    {
        var trip = Dispatched();

        trip.Deliveries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Version = 1, State = TripDeliveryState.Sending, Cause = (string?)null });
    }

    [Fact]
    public void ReportDelivered_ASendingVersion_IsDelivered_AndRaisesNothing()
    {
        var trip = Dispatched();
        trip.ClearDomainEvents();

        trip.ReportDelivered(1, At).Should().Be(TripDeliveryReport.Recorded);

        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.Delivered);
        trip.Deliveries.Single().ReportedAt.Should().Be(At);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReportNotDelivered_ASendingVersion_IsNotDelivered_WithItsCause_AndRaisesTripNotDelivered()
    {
        var trip = Dispatched();
        trip.ClearDomainEvents();

        trip.ReportNotDelivered(1, "The Driver app answered 503.", Report, At).Should().Be(TripDeliveryReport.Recorded);

        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.NotDelivered);
        trip.Deliveries.Single().Cause.Should().Be("The Driver app answered 503.");
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripNotDeliveredEvent>().Subject;
        raised.Trip.Should().BeSameAs(trip);
        raised.Version.Should().Be(1);
        raised.DriverId.Should().Be(Joost.Id);
        raised.Cause.Should().Be("The Driver app answered 503.");
        raised.CorrelationId.Should().Be(Report);
        raised.ReportedAt.Should().Be(At);
    }

    [Fact]
    public void Report_ForAVersionTheTripNeverHad_IsUnknown_AndChangesNothing()
    {
        var trip = Dispatched();
        trip.ClearDomainEvents();

        trip.ReportDelivered(2, At).Should().Be(TripDeliveryReport.UnknownVersion);
        trip.ReportNotDelivered(0, "x", Report, At).Should().Be(TripDeliveryReport.UnknownVersion);

        trip.Deliveries.Single().State.Should().Be(TripDeliveryState.Sending);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Report_ForAVersionThatIsNoLongerSending_IsIgnored()
    {
        var trip = Dispatched();
        trip.ReportNotDelivered(1, "first cause", Report, At);
        trip.ClearDomainEvents();

        trip.ReportDelivered(1, At.AddMinutes(1)).Should().Be(TripDeliveryReport.Ignored);
        trip.ReportNotDelivered(1, "second cause", Report, At.AddMinutes(1)).Should().Be(TripDeliveryReport.Ignored);

        trip.Deliveries.Single().Cause.Should().Be("first cause");
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReportNotDelivered_ForAnOlderVersion_IsStoredAgainstThatVersion_PublishesNothing_AndChangesNothingElse()
    {
        var trip = Dispatched(versions: 2);
        trip.ClearDomainEvents();

        trip.ReportNotDelivered(1, "old cause", Report, At).Should().Be(TripDeliveryReport.Recorded);

        trip.Deliveries.Single(delivery => delivery.Version == 1).State.Should().Be(TripDeliveryState.NotDelivered);
        trip.Deliveries.Single(delivery => delivery.Version == 2).State.Should().Be(TripDeliveryState.Sending);
        trip.Version.Should().Be(2);
        trip.DomainEvents.Should().BeEmpty("the Driver app holds a newer version, so nothing is lost");
    }

    [Fact]
    public void ReportDelivered_ForAnOlderVersion_IsStoredAgainstThatVersion_AndChangesNothingElse()
    {
        var trip = Dispatched(versions: 2);

        trip.ReportDelivered(1, At).Should().Be(TripDeliveryReport.Recorded);

        trip.Deliveries.Single(delivery => delivery.Version == 1).State.Should().Be(TripDeliveryState.Delivered);
        trip.Deliveries.Single(delivery => delivery.Version == 2).State.Should().Be(TripDeliveryState.Sending);
    }

    [Fact]
    public void EnsureCanSendAgain_ANotDeliveredLatestVersion_IsNull()
    {
        var trip = Dispatched();
        trip.ReportNotDelivered(1, "x", Report, At);

        trip.EnsureCanSendAgain(1).Should().BeNull();
    }

    [Fact]
    public void EnsureCanSendAgain_IsRefusedForADraft_AnUnknownVersion_AnOlderVersion_AndAVersionThatIsNotNotDelivered()
    {
        Draft().EnsureCanSendAgain(1)!.Code.Should().Be(TripRefusalCode.TripNotDispatched);

        var trip = Dispatched(versions: 2);
        trip.ReportNotDelivered(1, "x", Report, At);
        trip.EnsureCanSendAgain(3)!.Code.Should().Be(TripRefusalCode.TripVersionNotFound);
        trip.EnsureCanSendAgain(1)!.Code.Should().Be(TripRefusalCode.TripVersionNotLatest);
        trip.EnsureCanSendAgain(2)!.Code.Should().Be(TripRefusalCode.TripVersionNotUndelivered, "version 2 is still Sending");
        trip.ReportDelivered(2, At);
        trip.EnsureCanSendAgain(2)!.Code.Should().Be(TripRefusalCode.TripVersionNotUndelivered, "version 2 was delivered");
    }

    [Fact]
    public void SendAgain_SetsTheSameVersionSendingAgain_AndRaisesTripDispatchedWithTheSameVersionAndANewCorrelationId()
    {
        var trip = Dispatched();
        trip.ReportNotDelivered(1, "x", Report, At);
        trip.ClearDomainEvents();
        var again = new CorrelationId("corr-again");

        trip.SendAgain(1, again, At.AddMinutes(10), Truck, Joost, []);

        trip.Version.Should().Be(1);
        trip.Deliveries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Version = 1, State = TripDeliveryState.Sending, Cause = (string?)null });
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.Trip.Version.Should().Be(1);
        raised.CorrelationId.Should().Be(again);
        raised.DispatchedAt.Should().Be(At.AddMinutes(10));
    }

    [Fact]
    public void SendAgain_WhenRefused_IsABug()
    {
        var trip = Dispatched();

        trip.Invoking(t => t.SendAgain(1, new CorrelationId("c"), At, Truck, Joost, [])).Should().Throw<InvalidOperationException>();
    }

    private static Trip Draft()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, Truck.Id, Joost.Id, new IdempotencyKey("k"));
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        trip.AddShipment(ShipmentId.New());
        return trip;
    }

    // A Dispatched Trip at version 1. Amend isn't built yet, so a later version is put in place the way Amend will: the Version rises and a Sending delivery joins.
    private static Trip Dispatched(int versions = 1)
    {
        var trip = Draft();
        trip.Dispatch(new CorrelationId("corr-dispatch"), At, Truck, Joost, []);
        for (var version = 2; version <= versions; version++)
        {
            typeof(Trip).GetProperty(nameof(Trip.Version))!.SetValue(trip, version);
            var deliveries = (List<TripVersionDelivery>)typeof(Trip).GetField("_deliveries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(trip)!;
            deliveries.Add(TripVersionDelivery.Start(version));
        }

        return trip;
    }
}
