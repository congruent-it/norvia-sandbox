using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.DriverApp.Application.Faults;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Trips;

/// <summary>The Driver app keeps the latest version of each Trip the Trip dispatch Logic App sends (P11, ADR 0015).</summary>
public sealed class ReceiveTripVersionHandlerTests
{
    private readonly IRepository<DriverTrip> _trips = Substitute.For<IRepository<DriverTrip>>();
    private readonly IDriverAppFaults _faults = Substitute.For<IDriverAppFaults>();
    private readonly List<DriverTrip> _stored = [];

    public ReceiveTripVersionHandlerTests()
    {
        _trips.GetByIdAsync(Arg.Any<TripNumber>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_stored.SingleOrDefault(trip => trip.Id == call.Arg<TripNumber>())));
        _trips.AddAsync(Arg.Any<DriverTrip>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _stored.Add(call.Arg<DriverTrip>());
            return Task.FromResult(call.Arg<DriverTrip>());
        });
    }

    private static ReceiveTripVersionCommand Version(int version = 1, string driverId = "DRV-0002", string tripNumber = "TR-0000012", string duration = "PT4H", IReadOnlyList<StopDto>? stops = null) =>
        new(tripNumber, version, "corr", new DateOnly(2026, 10, 13), new TimeOnly(9, 0), duration, new TripVehicleDto("VEH-0002", "NV-123-A", "RigidTruck"), driverId, "Joost Bakker", stops ?? [Stop(1)]);

    private static StopDto Stop(int sequence) => new(
        sequence, StopKindDto.Collection, "SH-0000012", "TO-0000003", "SHP-0001",
        new StopAddressDto("Kade 1", "3011 AA", "Rotterdam", "NL"),
        new StopWindowDto(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(2))),
        [new StopGoodsLineDto("pallet", 2, 500m, new StopDimensionsDto(120, 80, 100))],
        new StopConsigneeDto("Consignee BV", "Contact", "+31 10 123", "c@example.com"));

    private Task<Result<TripVersionOutcome>> Receive(ReceiveTripVersionCommand command) =>
        new ReceiveTripVersionHandler(_trips, _faults).Handle(command, CancellationToken.None).AsTask();

    [Fact]
    public async Task ANewTrip_IsStored_WithItsStopsAsTheyWereSent()
    {
        var result = await Receive(Version());

        result.Value.Should().Be(TripVersionOutcome.Stored);
        var trip = _stored.Should().ContainSingle().Subject;
        trip.Version.Should().Be(1);
        trip.Details.PlannedDrivingDuration.Should().Be(TimeSpan.FromHours(4));
        var stop = trip.Details.Stops.Should().ContainSingle().Subject;
        stop.Kind.Should().Be(StopKind.Collection);
        stop.ShipmentNumber.Should().Be("SH-0000012");
        stop.Address.Should().Be(new StopAddress("Kade 1", "3011 AA", "Rotterdam", "NL"));
        stop.GoodsLines.Single().DimensionsCm.Should().Be(new StopDimensions(120, 80, 100));
        stop.Consignee!.Name.Should().Be("Consignee BV");
    }

    [Fact]
    public async Task AHigherVersion_ReplacesTheHeldOne()
    {
        await Receive(Version(1));

        var result = await Receive(Version(2, driverId: "DRV-0001", stops: []));

        result.Value.Should().Be(TripVersionOutcome.Stored);
        _stored.Single().Version.Should().Be(2);
        _stored.Single().DriverId.Should().Be(new DriverId("DRV-0001"));
        await _trips.Received(1).UpdateAsync(_stored.Single(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task AnEqualOrLowerVersion_IsIgnored(int version)
    {
        await Receive(Version(2));

        var result = await Receive(Version(version, driverId: "DRV-0001"));

        result.Value.Should().Be(TripVersionOutcome.Ignored);
        _stored.Single().DriverId.Should().Be(new DriverId("DRV-0002"));
        await _trips.DidNotReceive().UpdateAsync(Arg.Any<DriverTrip>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhileTheDriverAppIsUnavailable_EverythingIsRefused_AMalformedVersionIncluded()
    {
        _faults.IsUnavailable.Returns(true);

        (await Receive(Version())).Status.Should().Be(ResultStatus.Unavailable);
        (await Receive(Version(0))).Status.Should().Be(ResultStatus.Unavailable);

        _stored.Should().BeEmpty();
    }

    [Fact]
    public async Task AMalformedVersion_IsInvalid_WithEveryFaultNamed_AndStoresNothing()
    {
        var badStop = Stop(0) with { ShipmentNumber = "SH-1", TransportOrderNumber = "TO-1", ShipperId = " " };
        var command = Version(0, driverId: " ", tripNumber: "nope", duration: "PT25H", stops: [badStop]) with
        {
            CorrelationId = " ",
            DriverName = " ",
            Vehicle = new TripVehicleDto(" ", "NV-123-A", "Hovercraft"),
        };

        var result = await Receive(command);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Select(error => error.Identifier).Should().BeEquivalentTo(
            "tripNumber", "version", "correlationId", "driver.driverId", "driver.name", "plannedDrivingDuration", "vehicle", "vehicle.vehicleType",
            "stops[0].sequence", "stops[0].shipmentNumber", "stops[0].transportOrderNumber", "stops[0].shipperId");
        _stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("PT0S")]
    [InlineData("not a duration")]
    public async Task ADrivingDurationThatIsNothingOrNotADuration_IsInvalid(string duration)
    {
        (await Receive(Version(duration: duration))).ValidationErrors.Select(error => error.Identifier).Should().Equal("plannedDrivingDuration");
    }
}
