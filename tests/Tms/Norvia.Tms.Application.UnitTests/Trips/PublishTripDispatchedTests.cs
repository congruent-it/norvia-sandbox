using System.Text.Json;
using System.Text.Json.Nodes;
using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips.Dispatch;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class PublishTripDispatchedTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly Vehicle Truck = new(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true);
    private static readonly Driver Joost = new(new DriverId("DRV-0001"), "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.C }, true);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.TripDispatchedSamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateTripDispatched(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Fact]
    public void Schema_MessageWithoutItsVehicle_DoesNotValidate()
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripDispatchedSamples().First()).AsObject();
        message.Remove("vehicle");

        CanonicalSchemas.ValidateTripDispatched(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_MessageWithAnUnknownStopKind_DoesNotValidate()
    {
        var message = CanonicalSchemas.ReadSample(CanonicalSchemas.TripDispatchedSamples().First(name => name.EndsWith("two-shipments.json", StringComparison.Ordinal))).AsObject();
        message["stops"]![0]!["kind"] = "Pause";

        CanonicalSchemas.ValidateTripDispatched(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TripDispatched_AddsExactlyOneTripDispatchedToTheOutbox()
    {
        var dispatched = Dispatch(out _);

        await new PublishTripDispatched(_outbox).Handle(dispatched, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
        _outbox.Received(1).Add(Arg.Any<TripDispatchedV1>());
    }

    [Fact]
    public void From_DispatchedTrip_KeepsToTheSchema_WithTheWholeTripAndItsStopsInOrder()
    {
        var dispatched = Dispatch(out var shipments);

        var message = TripDispatchedV1.From(dispatched);

        var body = JsonSerializer.SerializeToNode(message, message.GetType(), IntegrationEventJson)!;
        var results = CanonicalSchemas.ValidateTripDispatched(body);
        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results) + body.ToJsonString());
        message.TripNumber.Should().Be("TR-0000007");
        message.Version.Should().Be(1);
        message.CorrelationId.Should().Be("corr-dispatch");
        message.Day.Should().Be(Day);
        message.PlannedStart.Should().Be(new TimeOnly(7, 30));
        message.PlannedDrivingDuration.Should().Be("PT6H");
        message.Vehicle.Should().Be(new TripVehicle("VEH-0002", "TT-201-R", "RigidTruck"));
        message.Driver.Should().Be(new TripDriver("DRV-0001", "Joost Bakker"));
        message.Stops.Select(stop => (stop.Sequence, stop.Kind, stop.ShipmentNumber)).Should().Equal(
            (1, "Collection", shipments[0].Number.Value),
            (2, "Collection", shipments[1].Number.Value),
            (3, "Delivery", shipments[1].Number.Value),
            (4, "Delivery", shipments[0].Number.Value));
        message.Stops[0].Consignee.Should().BeNull("a collection Stop has no Consignee");
        message.Stops[3].Consignee!.Name.Should().Be("Café De Zwaan");
        message.Stops[3].Address!.City.Should().Be("Utrecht");
        message.Stops[0].Address!.City.Should().Be("Rotterdam");
    }

    [Fact]
    public void TripDispatched_Tracks_TheTripNumber_OnTheTripEventsTopic()
    {
        var message = TripDispatchedV1.From(Dispatch(out _));

        message.Topic.Should().Be("trip-events");
        message.EventType.Should().Be("TripDispatched");
        message.EventVersion.Should().Be(1);
        message.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TripDispatched",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-dispatch",
            ["flow"] = "trip-dispatch",
            ["tripNumber"] = "TR-0000007",
        });
    }

    private static readonly JsonSerializerOptions IntegrationEventJson = new(JsonSerializerDefaults.Web);

    // Two Shipments on a Trip, their Stops reordered so both collections come before both deliveries.
    private static TripDispatchedEvent Dispatch(out List<Shipment> shipments)
    {
        shipments = [];
        var onTrip = new List<DispatchedShipment>();
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), Day, Truck.Id, Joost.Id, new IdempotencyKey("k"));
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        foreach (var sequence in new[] { 12, 13 })
        {
            var order = Samples.AcceptedOrder(sequence: sequence);
            var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: sequence);
            shipment.Release(order, new IdempotencyKey($"r{sequence}"), new CorrelationId("corr-release"), Samples.Now);
            shipment.ClearDomainEvents();
            shipments.Add(shipment);
            onTrip.Add(new DispatchedShipment(shipment, order.Details));
            trip.AddShipment(shipment.Id);
        }

        trip.ReorderStops(
        [
            (shipments[0].Id, StopKind.Collection),
            (shipments[1].Id, StopKind.Collection),
            (shipments[1].Id, StopKind.Delivery),
            (shipments[0].Id, StopKind.Delivery),
        ]).Should().BeNull();
        trip.Dispatch(new CorrelationId("corr-dispatch"), Samples.Now, Truck, Joost, onTrip);
        return (TripDispatchedEvent)trip.DomainEvents.Single();
    }
}
