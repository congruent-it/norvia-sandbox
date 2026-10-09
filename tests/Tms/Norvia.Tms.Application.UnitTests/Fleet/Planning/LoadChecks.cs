using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

/// <summary>Builds the load check services over lists, for the handlers that use them.</summary>
internal static class LoadChecks
{
    public static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static DayLoads LoadsOver(List<Shipment> shipments, List<TransportOrder> orders, IDriverDirectory drivers, IVehicleDirectory vehicles) =>
        new(Repository(shipments), Repository(orders), drivers, vehicles);

    /// <summary>Warnings for Planning days that don't exist, so no assignment under test gets one.</summary>
    public static LoadCheckWarnings NoWarnings(TimeProvider time) =>
        Warnings([], [], [], [], [], time);

    public static LoadCheckWarnings Warnings(
        List<PlanningDay> days, List<TransportOrder> orders, List<Shipment> shipments, Driver[] drivers, Vehicle[] vehicles, TimeProvider time)
    {
        var driverDirectory = Substitute.For<IDriverDirectory>();
        driverDirectory.ListActive().Returns(drivers);
        var vehicleDirectory = Substitute.For<IVehicleDirectory>();
        vehicleDirectory.ListActive().Returns(vehicles);
        return new LoadCheckWarnings(Repository(days), Repository(orders), LoadsOver(shipments, orders, driverDirectory, vehicleDirectory), new PlanningHorizonRules(14, Amsterdam), time);
    }

    public static IReadRepository<T> Repository<T>(List<T> stored)
        where T : class, IAggregateRoot
    {
        var repository = Substitute.For<IReadRepository<T>>();
        repository.ListAsync(Arg.Any<ISpecification<T>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<T>>().Evaluate(stored).ToList());
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<T>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<T>>().Evaluate(stored).FirstOrDefault());
        return repository;
    }
}
