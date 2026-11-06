using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Trips;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>
/// A run whose orders all stay with the Own fleet, against a TMS that plans, releases, builds and dispatches Trips and a Driver app that takes every report: the
/// ground the Trip and Driver stages are tested on. Each test says what differs by changing a stub before it runs.
/// </summary>
internal sealed class FleetRun
{
    public const int Orders = 6;

    public readonly IRepository<SimulationRun> Runs = Substitute.For<IRepository<SimulationRun>>();
    public readonly IRepository<LogEntry> Log = Substitute.For<IRepository<LogEntry>>();
    public readonly IPartnerApi Partner = Substitute.For<IPartnerApi>();
    public readonly ITmsApi Tms = Substitute.For<ITmsApi>();
    public readonly IDriverApp Drivers = Substitute.For<IDriverApp>();
    public readonly IErpApi Erp = Substitute.For<IErpApi>();
    public readonly IShipperInbox Inbox = Substitute.For<IShipperInbox>();
    public readonly IWorldSource World = Substitute.For<IWorldSource>();
    public readonly FakeTimeProvider Time = new(TestData.Now);
    public readonly List<LogEntry> Lines = [];
    public readonly List<string> Released = [];
    public readonly List<(TripDraft Draft, string Key, string Number)> Created = [];
    public readonly List<string> Dispatched = [];

    /// <summary>The Drivers' calls, in the order they were made: the action and the Trip, and the Shipment for a Stop.</summary>
    public readonly List<(string Action, SimulatedDriver Driver, string Trip, string? Shipment)> Calls = [];

    /// <summary>What the Driver app shows of a Trip once it has one: Completed, unless a test says otherwise.</summary>
    public Func<string, DriverTripSnapshot?> Snapshot { get; set; }

    public FleetRun()
    {
        Snapshot = trip => new DriverTripSnapshot(trip, 1, DriverTripStatus.Completed, 0);
        World.Load().Returns(TestData.FleetWorld);
        Log.AddAsync(Arg.Do<LogEntry>(line => { lock (Lines) { Lines.Add(line); } }), Arg.Any<CancellationToken>()).Returns(call => call.Arg<LogEntry>());
        Partner.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => new BookingAnswer(LogOutcome.Accepted, $"corr-{call.Arg<PlannedBooking>().Step}", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));
        Tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<FleetDay>?>(Days()));
        Tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.Planned, $"plan-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[3..]}", null));
        Tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (Released) { Released.Add(call.ArgAt<string>(0)); }
            return new PlannerAnswer(LogOutcome.Released, $"rel-{call.ArgAt<string>(0)}", call.ArgAt<string>(0), null);
        });
        Tms.CreateTripAsync(Arg.Any<TripDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var number = $"TR-{Created.Count + 1:0000000}";
            Created.Add((call.Arg<TripDraft>(), call.ArgAt<string>(1), number));
            return new PlannerAnswer(LogOutcome.TripBuilt, null, null, null, TripNumber: number);
        });
        Tms.DispatchTripAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Dispatched.Add(call.ArgAt<string>(0));
            return new PlannerAnswer(LogOutcome.TripDispatched, $"disp-{call.ArgAt<string>(0)}", null, null, TripNumber: call.ArgAt<string>(0));
        });
        Tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>(OpenExceptions()));
        Drivers.GetTripsAsync(Arg.Any<SimulatedDriver>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<DriverTripSnapshot>?>([.. Created.Select(created => Snapshot(created.Number)).OfType<DriverTripSnapshot>()]));
        Drivers.StartTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Reported(DriverActions.StartTrip, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), null));
        Drivers.CollectAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Reported(DriverActions.Collected, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        Drivers.FailCollectionAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Reported(DriverActions.CollectionFailed, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        Drivers.FailDeliveryAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Reported(DriverActions.DeliveryFailed, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        Drivers.ReportDamageAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Reported(DriverActions.ReportDamage, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        Drivers.DeliverAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DriverDelivery>(), Arg.Any<CancellationToken>())
            .Returns(call => Reported(DriverActions.Deliver, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        Drivers.EndTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Reported(DriverActions.EndTrip, call.Arg<SimulatedDriver>(), call.ArgAt<string>(1), null));
    }

    /// <summary>Today in Norvia's time, as the Trips of today are told from the others.</summary>
    public static DateOnly Today => PickupSlot.Today(TestData.Now);

    /// <summary>The Own fleet's open days with every Driver on a Shift, unless a test gives other days.</summary>
    public Func<IReadOnlyList<FleetDay>> Days { get; set; } = () => Week(TestData.Anouk, TestData.Bram, TestData.Sanne);

    public static IReadOnlyList<FleetDay> Week(params SimulatedDriver[] working) =>
    [
        .. Enumerable.Range(0, 10).Select(offset => new FleetDay(Today.AddDays(offset), 100_000, 1_000, 0, 0)
        {
            Shifts = [.. working.Select(driver => new DriverShift(driver.Id, new TimeOnly(6, 0), new TimeOnly(14, 30)))],
        }),
    ];

    public static SimulationSettings Settings(int todayPercent = 100, DriverShares? drivers = null, int orders = Orders, InvoicingShares? invoicing = null) =>
        new(orders, 0, todayPercent, 11, RunMode.Burst, TimeSpan.Zero, null, AssignedPercent: 100, OwnFleetPercent: 100, ReassignPercent: 0)
        {
            Carriers = new CarrierShares(ReleasePercent: 0),
            Drivers = drivers ?? new DriverShares(0, 0, 0, 0),
            Invoicing = invoicing ?? new InvoicingShares(InvoicePercent: 0, CreditNotePercent: 0),
        };

    public ExecuteRunHandler Handler() => new(
        Runs, Log, Partner, Tms, World, new SimulationLimits(50, 3), Time, new CarrierPacing(TimeSpan.Zero, 1), Drivers, new DriverPacing(TimeSpan.Zero, 2), Erp, Inbox, Substitute.For<ICarrierInbox>());

    public async Task<SimulationRun> RunAsync(SimulationSettings settings, CancellationTokenSource? stop = null, bool driverPools = false)
    {
        var run = driverPools ? TestData.RunWithDriverPools(settings) : TestData.Run(settings);
        Runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        (await Handler().Handle(new ExecuteRunCommand(run.Id, stop?.Token ?? CancellationToken.None), CancellationToken.None)).IsSuccess.Should().BeTrue();
        return run;
    }

    /// <summary>The Shipments that were put on Trips, by Shipment number.</summary>
    public IEnumerable<string> OnTrips => Created.SelectMany(created => created.Draft.Shipments.Select(shipment => shipment.ShipmentNumber));

    /// <summary>The Exceptions the TMS has opened so far: one for each failure or damage the Drivers reported. Reservations show as a Reservations Exception.</summary>
    public Func<IReadOnlyList<OpenExceptionSummary>> OpenExceptionsNow { get; set; } = () => [];

    private IReadOnlyList<OpenExceptionSummary> OpenExceptions()
    {
        lock (Calls)
        {
            return
            [
                .. Calls.Where(call => call.Shipment is not null && call.Action is DriverActions.CollectionFailed or DriverActions.DeliveryFailed or DriverActions.ReportDamage)
                    .Select(call => new OpenExceptionSummary($"EXC-{call.Shipment![3..]}-{call.Action.Length}", call.Shipment!, call.Action == DriverActions.ReportDamage ? "Damaged" : call.Action == DriverActions.CollectionFailed ? "Collection failed" : "Delivery failed")),
                .. OpenExceptionsNow(),
            ];
        }
    }

    private Task<DriverCallAnswer> Reported(string action, SimulatedDriver driver, string trip, string? shipment)
    {
        lock (Calls) { Calls.Add((action, driver, trip, shipment)); }
        return Task.FromResult(new DriverCallAnswer(LogOutcome.DriverReported, $"drv-{action}-{trip}-{shipment}", null));
    }
}
