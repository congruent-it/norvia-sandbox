extern alias SimulatorWeb;

using Ardalis.Result;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>What the tests do with the Simulator's runs, through the doors its pages use.</summary>
internal static class Runs
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>A run with Trips waits for the Logic Apps' stand-ins to carry each Trip to the Driver app and each report to the TMS, so it takes longer than a run without.</summary>
    public static readonly TimeSpan TripPatience = TimeSpan.FromSeconds(180);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static int _seed = Environment.TickCount;

    /// <summary>A seed of this test's own, so no two tests make the same plan.</summary>
    /// <summary>The runs of these tests are about the stages before Finance's; the invoicing stage waits for deliveries to become Waiting lines, which would only slow them.</summary>
    private static readonly InvoicingShares NoInvoicing = new(InvoicePercent: 0, CreditNotePercent: 0);

    public static int NextSeed() => Interlocked.Increment(ref _seed);

    public static SimulationSettings Burst(int orders, int badSharePercent, int seed) =>
        new(orders, badSharePercent, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero) { TripPercent = 0, Invoicing = NoInvoicing };

    /// <summary>A run of good bookings whose Planner assigns, sends to Own fleet and reassigns the given shares of the orders.</summary>
    public static SimulationSettings Planned(int orders, int seed, int assignedPercent, int ownFleetPercent, int reassignPercent, int badSharePercent = 0) =>
        new(orders, badSharePercent, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero,
            AssignedPercent: assignedPercent, OwnFleetPercent: ownFleetPercent, ReassignPercent: reassignPercent)
        {
            // These runs test the Planner: the Shipments stay Planned, and the Carrier stage has its own tests (#354), the Trips and Drivers theirs (#356).
            Carriers = new CarrierShares(ReleasePercent: 0),
            TripPercent = 0,
            Invoicing = NoInvoicing,
        };

    /// <summary>
    /// A run of good bookings that the Planner assigns all to Carriers, whose Carrier stage has the given shares. The shares ask for nothing the
    /// test does not name: no release, answer, tracking or delivery unless the test says so.
    /// </summary>
    public static SimulationSettings Carried(int orders, int seed, CarrierShares carriers) =>
        new(orders, BadSharePercent: 0, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero,
            AssignedPercent: 100, OwnFleetPercent: 0, ReassignPercent: 0)
        {
            Carriers = carriers,
            TripPercent = 0,
            Invoicing = NoInvoicing,
        };

    /// <summary>
    /// A run of good bookings that the Planner assigns all to the Own fleet, puts on Trips and dispatches, of which the given share is collected today and so
    /// worked by its Drivers. No Shipment goes to a Carrier.
    /// </summary>
    public static SimulationSettings Tripped(int orders, int seed, int collectedTodayPercent, DriverShares drivers) =>
        new(orders, BadSharePercent: 0, collectedTodayPercent, seed, RunMode.Burst, TimeSpan.Zero,
            AssignedPercent: 100, OwnFleetPercent: 100, ReassignPercent: 0)
        {
            Carriers = new CarrierShares(ReleasePercent: 0),
            Drivers = drivers,
            TripPercent = 100,
            Invoicing = NoInvoicing,
        };

    /// <summary>
    /// A run of good bookings that the Planner assigns all to Carriers, whose Carrier release, confirm, track and deliver every Shipment with a Proof of
    /// delivery and no Exception, so each Transport Order becomes Billable and Finance has something to invoice. Finance has the given shares.
    /// </summary>
    public static SimulationSettings Invoiced(int orders, int seed, InvoicingShares invoicing) =>
        new(orders, BadSharePercent: 0, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero,
            AssignedPercent: 100, OwnFleetPercent: 0, ReassignPercent: 0)
        {
            Carriers = new CarrierShares(
                ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
                DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0),
            TripPercent = 0,
            Invoicing = invoicing,
        };

    /// <summary>The lines in which Finance called the ERP or a Shipper Persona's invoices inbox took a document.</summary>
    public static bool IsInvoicing(LogLine line) => LogOutcomes.Invoicing.Contains(line.Outcome);

    /// <summary>A run that sends its first booking at once and the next a minute later.</summary>
    public static SimulationSettings Paced(int orders, int seed) =>
        new(orders, BadSharePercent: 0, CollectedTodayPercent: 30, seed, RunMode.Paced, TimeSpan.FromMinutes(orders)) { TripPercent = 0, Invoicing = NoInvoicing };

    public static async Task<RunSummary> StartAsync(this ISimulatorRequests simulator, SimulationSettings settings, CancellationToken cancellationToken)
    {
        var started = await simulator.StartAsync(settings, "integration tests", cancellationToken);
        started.Status.Should().Be(ResultStatus.Ok, string.Join("; ", started.Errors));
        return started.Value;
    }

    /// <summary>Waits until the run is no longer active.</summary>
    public static Task<RunSummary> FinishedAsync(this ISimulatorRequests simulator, Guid runId, CancellationToken cancellationToken, TimeSpan? patience = null) =>
        EventuallyAsync(async () =>
        {
            var run = (await simulator.GetRunAsync(new SimulationRunId(runId), cancellationToken)).Value.Run;
            return run.IsActive ? null : run;
        }, cancellationToken, patience);

    /// <summary>Previews the plan, runs it to its end and reads its log, as a page would.</summary>
    public static async Task<(PlanPreview Preview, RunSummary Run, IReadOnlyList<LogLine> Log)> RunToEndAsync(
        this SimulatorHost simulator, SimulationSettings settings, CancellationToken cancellationToken, TimeSpan? patience = null)
    {
        var preview = (await simulator.Requests.PreviewAsync(settings, cancellationToken)).Value;
        var started = await simulator.Requests.StartAsync(settings, cancellationToken);
        var run = await simulator.Requests.FinishedAsync(started.Id, cancellationToken, patience);
        return (preview, run, await simulator.Requests.LogAsync(run.Id, cancellationToken));
    }

    /// <summary>Waits until the run's log has a line that satisfies the predicate, and returns the log.</summary>
    public static Task<IReadOnlyList<LogLine>> LogWithAsync(
        this ISimulatorRequests simulator, Guid runId, Func<LogLine, bool> predicate, CancellationToken cancellationToken) =>
        EventuallyAsync(async () =>
        {
            var log = await simulator.ListLogAsync(new SimulationRunId(runId), new LogFilter(), cancellationToken);
            return log.Any(predicate) ? log : null;
        }, cancellationToken);

    public static async Task<IReadOnlyList<LogLine>> LogAsync(this ISimulatorRequests simulator, Guid runId, CancellationToken cancellationToken) =>
        await simulator.ListLogAsync(new SimulationRunId(runId), new LogFilter(), cancellationToken);

    /// <summary>The lines in which a Persona sent a booking, as opposed to the Simulator's own notes.</summary>
    public static bool IsBooking(LogLine line) => LogOutcomes.Booking.Contains(line.Outcome);

    /// <summary>The lines in which a Carrier called the Partner API.</summary>
    public static bool IsCarrier(LogLine line) => LogOutcomes.Carrier.Contains(line.Outcome);

    /// <summary>The lines in which a Driver called the Driver app API.</summary>
    public static bool IsDriver(LogLine line) => LogOutcomes.Driver.Contains(line.Outcome);

    /// <summary>The lines in which the Planner called the TMS API.</summary>
    public static bool IsPlanning(LogLine line) => LogOutcomes.Planning.Contains(line.Outcome);

    /// <summary>Waits until the condition holds. The TMS processes what the Carriers send after it accepts it, so what it made of it shows a little later.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken) =>
        await EventuallyAsync(async () => await condition() ? new object() : null, cancellationToken);

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> attempt, CancellationToken cancellationToken, TimeSpan? patience = null)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(patience ?? Patience);
        while (true)
        {
            if (await attempt() is { } found)
            {
                return found;
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }
}
