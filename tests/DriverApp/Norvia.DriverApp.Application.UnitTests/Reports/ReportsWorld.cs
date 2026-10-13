using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>
/// A Driver app with the Trips and reports of <see cref="Trips"/> and <see cref="Reports"/> in place of the database, a clock on Tuesday 13 October 2026 at
/// 06:05 UTC (08:05 in Norvia's time) and an outbox that records what was queued, in the order it was queued relative to the save.
/// </summary>
internal sealed class ReportsWorld
{
    public static readonly DriverId Joost = new("DRV-0002");
    public static readonly DriverId Anouk = new("DRV-0001");
    public static readonly TripNumber TripToday = new("TR-0000012");

    public ReportsWorld()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
        Trips.Add(Trip(TripToday, 2, Joost, new DateOnly(2026, 10, 13)));
        Trips.Add(Trip(new TripNumber("TR-0000013"), 1, Joost, new DateOnly(2026, 10, 14)));
        Trips.Add(Trip(new TripNumber("TR-0000014"), 1, Anouk, new DateOnly(2026, 10, 13)));

        TripRepository.GetByIdAsync(Arg.Any<TripNumber>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Trips.SingleOrDefault(trip => trip.Id == call.Arg<TripNumber>())));
        TripRepository.ListAsync(Arg.Any<ISpecification<DriverTrip, DriverTripRow>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<ISpecification<DriverTrip, DriverTripRow>>().Evaluate(Trips).ToList()));
        ReportRepository.GetByIdAsync(Arg.Any<ReportId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Reports.SingleOrDefault(report => report.Id == call.Arg<ReportId>())));
        ReportRepository.FirstOrDefaultAsync(Arg.Any<ISpecification<DriverReport>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<ISpecification<DriverReport>>().Evaluate(Reports).FirstOrDefault()));
        ReportRepository.ListAsync(Arg.Any<ISpecification<DriverReport, DriverReportRow>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<ISpecification<DriverReport, DriverReportRow>>().Evaluate(Reports).ToList()));
        ReportRepository.AddAsync(Arg.Any<DriverReport>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var report = call.Arg<DriverReport>();
                Reports.Add(report);
                Saved.AddRange(Queued.Skip(Saved.Count));
                return Task.FromResult(report);
            });
        ReportRepository.AddRangeAsync(Arg.Any<IEnumerable<DriverReport>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var added = call.Arg<IEnumerable<DriverReport>>().ToList();
                Reports.AddRange(added);
                Saved.AddRange(Queued.Skip(Saved.Count));
                return Task.FromResult<IEnumerable<DriverReport>>(added);
            });
        ReadReportRepository.ListAsync(Arg.Any<ISpecification<DriverReport, DriverReportRow>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<ISpecification<DriverReport, DriverReportRow>>().Evaluate(Reports).ToList()));
        Outbox.When(outbox => outbox.Add(Arg.Any<DriverAppEvent>())).Do(call => Queued.Add(call.Arg<DriverAppEvent>()));
    }

    public FakeTimeProvider Time { get; } = new();

    public List<DriverTrip> Trips { get; } = [];

    public List<DriverReport> Reports { get; } = [];

    /// <summary>Everything queued in the outbox.</summary>
    public List<DriverAppEvent> Queued { get; } = [];

    /// <summary>What was queued when the report was saved: only these reach Service Bus, in the same transaction.</summary>
    public List<DriverAppEvent> Saved { get; } = [];

    public IReadRepository<DriverTrip> TripRepository { get; } = Substitute.For<IReadRepository<DriverTrip>>();

    public IRepository<DriverReport> ReportRepository { get; } = Substitute.For<IRepository<DriverReport>>();

    public IReadRepository<DriverReport> ReadReportRepository { get; } = Substitute.For<IReadRepository<DriverReport>>();

    public IDriverEventOutbox Outbox { get; } = Substitute.For<IDriverEventOutbox>();

    /// <summary>Today's Trip, as a new version with a collection and a delivery Stop for each Shipment, the Planner's way: both collections, then both deliveries.</summary>
    public void GiveTodaysTripStops(params string[] shipments)
    {
        var trip = Trips.Single(trip => trip.Id == TripToday);
        var address = new StopAddress("Waalhaven 1", "3087 BM", "Rotterdam", "NL");
        List<TripStop> stops = [];
        foreach (var kind in new[] { StopKind.Collection, StopKind.Delivery })
        {
            foreach (var shipment in shipments)
            {
                stops.Add(new TripStop(
                    stops.Count + 1, kind, shipment, $"TO-{shipment[3..]}", "SHP-001", address, null, [new StopGoodsLine("Pallet", 2, 120, null)],
                    kind == StopKind.Delivery ? new StopConsignee("Bakker BV", null, "010-1234567", null) : null));
            }
        }

        trip.ReplaceWith(trip.Version + 1, Joost, trip.Details with { Stops = stops });
    }

    /// <summary>Today's Trip has been started and the TMS recorded it.</summary>
    public void StartTodaysTrip()
    {
        var start = DriverReport.StartTrip(new ReportId(Guid.NewGuid()), TripToday, Joost, 2, Time.GetUtcNow(), "start-corr");
        start.Record(Time.GetUtcNow());
        Reports.Add(start);
    }

    private static DriverTrip Trip(TripNumber number, int version, DriverId driver, DateOnly day) =>
        new(number, version, driver, new TripDetails(day, new TimeOnly(9, 0), TimeSpan.FromHours(4), new TripVehicle("VEH-0002", "NV-123-A", "RigidTruck"), "Joost Bakker", "corr", []));
}
