using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// Runs the Driver app's screens in memory with the Trips of <see cref="Trips"/> in place of the database, signed in the way
/// the environment says: Easy Auth's header in Azure, or the stand-in locally (ADR 0037).
/// </summary>
public sealed class DriverAppWebFactory(string environment, bool standIn) : WebApplicationFactory<Program>
{
    public const string AnoukId = "DRV-0001";
    public const string JoostId = "DRV-0002";

    /// <summary>The seed file the Driver app reads its Drivers from, as the AppHost points at it.</summary>
    private static readonly string SeedFile = Path.Combine(AppContext.BaseDirectory, "norvia.seed.json");

    public List<DriverTrip> Trips { get; } =
    [
        Trip("TR-0000001", AnoukId, "Anouk de Vries", new DateOnly(2026, 10, 14)),
        Trip("TR-0000002", AnoukId, "Anouk de Vries", new DateOnly(2026, 10, 13), WithStops("SH-0000001", "SH-0000002")),
        Trip("TR-0000003", JoostId, "Joost Bakker", new DateOnly(2026, 10, 14)),
    ];

    /// <summary>The Driver's reports, in place of the database.</summary>
    public List<DriverReport> Reports { get; } = [];

    /// <summary>Everything the Driver app queued in its outbox.</summary>
    public List<DriverAppEvent> Queued { get; } = [];

    /// <summary>What was queued when a report was saved: the outbox messages saved in the same transaction as their report.</summary>
    public List<DriverAppEvent> Saved { get; } = [];

    /// <summary>The files the Driver app put in the transit container, by blob name, with their content type.</summary>
    public Dictionary<string, (string ContentType, byte[] Content)> TransitFiles { get; } = [];

    /// <summary>Tuesday 13 October 2026, 08:05 in Norvia's time: the day of Anouk's Trip TR-0000002.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Seed:Path", SeedFile);
        builder.UseSetting("ConnectionStrings:driver-app", "Server=unused;Database=unused");
        builder.UseSetting("ConnectionStrings:driver-app-blobs", "UseDevelopmentStorage=true");
        if (standIn)
        {
            builder.UseSetting("LocalSignIn:Enabled", "true");
        }

        builder.ConfigureTestServices(services =>
        {
            var repository = Substitute.For<IReadRepository<DriverTrip>>();
            repository.ListAsync(Arg.Any<ISpecification<DriverTrip, DriverTripRow>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ISpecification<DriverTrip, DriverTripRow>>().Evaluate(Trips).ToList());
            repository.GetByIdAsync(Arg.Any<TripNumber>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Trips.SingleOrDefault(trip => trip.Id == call.Arg<TripNumber>())));
            services.AddScoped(_ => repository);

            var reportReads = Substitute.For<IReadRepository<DriverReport>>();
            reportReads.ListAsync(Arg.Any<ISpecification<DriverReport, DriverReportRow>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ISpecification<DriverReport, DriverReportRow>>().Evaluate(Reports).ToList());
            services.AddScoped(_ => reportReads);

            var reportWrites = Substitute.For<IRepository<DriverReport>>();
            reportWrites.ListAsync(Arg.Any<ISpecification<DriverReport, DriverReportRow>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ISpecification<DriverReport, DriverReportRow>>().Evaluate(Reports).ToList());
            reportWrites.FirstOrDefaultAsync(Arg.Any<ISpecification<DriverReport>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ISpecification<DriverReport>>().Evaluate(Reports).FirstOrDefault());
            reportWrites.AddAsync(Arg.Any<DriverReport>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var report = call.Arg<DriverReport>();
                Reports.Add(report);
                Saved.AddRange(Queued);
                Queued.Clear();
                return report;
            });
            reportWrites.AddRangeAsync(Arg.Any<IEnumerable<DriverReport>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var added = call.Arg<IEnumerable<DriverReport>>().ToList();
                Reports.AddRange(added);
                Saved.AddRange(Queued);
                Queued.Clear();
                return added;
            });
            services.AddScoped(_ => reportWrites);

            var transit = Substitute.For<IProofOfDeliveryTransit>();
            transit.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                TransitFiles[call.ArgAt<string>(0)] = (call.ArgAt<string>(1), call.ArgAt<ReadOnlyMemory<byte>>(2).ToArray());
                return Task.FromResult(call.ArgAt<string>(0));
            });
            services.AddSingleton(transit);

            var outbox = Substitute.For<IDriverEventOutbox>();
            outbox.When(queue => queue.Add(Arg.Any<DriverAppEvent>())).Do(call => Queued.Add(call.Arg<DriverAppEvent>()));
            services.AddScoped(_ => outbox);

            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Today's Trip of Anouk has been started and the TMS recorded it.</summary>
    public void StartAnouksTrip()
    {
        var start = DriverReport.StartTrip(new ReportId(Guid.NewGuid()), new TripNumber("TR-0000002"), new DriverId(AnoukId), 1, Time.GetUtcNow(), "start-corr");
        start.Record(Time.GetUtcNow());
        Reports.Add(start);
    }

    /// <summary>Anouk has collected both Shipments of today's Trip, and the TMS recorded it, so the deliveries are open.</summary>
    public void CollectAnouksShipments()
    {
        foreach (var shipment in new[] { "SH-0000001", "SH-0000002" })
        {
            var collected = DriverReport.ReportStop(
                new ReportId(Guid.NewGuid()), ReportKind.Collected, new StopRef(shipment, StopKind.Collection), new ReportOrigin(new TripNumber("TR-0000002"), new DriverId(AnoukId), 1, Time.GetUtcNow(), "collect-corr"), null);
            collected.Record(Time.GetUtcNow());
            Reports.Add(collected);
        }
    }

    /// <summary>A collection Stop for each Shipment, then a delivery Stop for each.</summary>
    private static TripStop[] WithStops(params string[] shipments) =>
    [
        .. new[] { StopKind.Collection, StopKind.Delivery }.SelectMany(kind => shipments.Select(shipment => new TripStop(
            0, kind, shipment, $"TO-{shipment[3..]}", "SHP-001", new StopAddress("Waalhaven 1", "3087 BM", "Rotterdam", "NL"), null,
            [new StopGoodsLine("Pallet", 2, 120, null)], kind == StopKind.Delivery ? new StopConsignee("Bakker BV", null, "010-1234567", null) : null))),
    ];

    private static DriverTrip Trip(string number, string driverId, string driverName, DateOnly day, params TripStop[] stops) =>
        new(new TripNumber(number), 1, new DriverId(driverId),
            new TripDetails(day, new TimeOnly(6, 0), TimeSpan.FromHours(5), new TripVehicle("VEH-0001", "NV-123-A", "RigidTruck"), driverName, "corr-1", [.. stops.Select((stop, index) => stop with { Sequence = index + 1 })]));
}
