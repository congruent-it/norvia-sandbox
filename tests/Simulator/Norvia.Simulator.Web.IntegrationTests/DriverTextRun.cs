extern alias SimulatorWeb;
using System.Text.RegularExpressions;
using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// One run of the Own fleet's Trips of today with Drivers that fail collections and deliveries and report damage, made once for the cases of
/// <see cref="DriverPersonaPoolTests"/>. The Simulator has the Agents on, so the plan's pools are written by the scripted fake model.
/// </summary>
public sealed partial class DriverTextRun(SimulatorFixture fixture) : IAsyncLifetime
{
    // The Own fleet of this class: no other test class puts Trips on today for them. DRV-0002's profile is terse Dutch.
    internal const string Shipper = "SHP-0001";
    internal const string Vehicle = "VEH-0002";
    internal const string Driver = "DRV-0002";

    private IAsyncDisposable? _worksToday;

    public SimulatorHost Simulator { get; private set; } = null!;

    public Guid RunId { get; private set; }

    public SimulationSettings Settings { get; } = Runs.Tripped(
        orders: 10, Runs.NextSeed(), collectedTodayPercent: 100,
        new DriverShares(CollectionFailedPercent: 20, DeliveryFailedPercent: 40, DamagedPercent: 30, ReservationsPercent: 0));

    /// <summary>The Driver actions that carry a text, with the kind of text each draws.</summary>
    public IReadOnlyList<(string Action, TextCategory Category)> Kinds { get; } =
    [
        (DriverActions.CollectionFailed, TextCategory.DriverCollectionFailedRemark),
        (DriverActions.DeliveryFailed, TextCategory.DriverDeliveryFailedRemark),
        (DriverActions.ReportDamage, TextCategory.DriverDamageRemark),
    ];

    [GeneratedRegex(@"pool (?<pool>\S+), index (?<index>\d+)")]
    private static partial Regex PoolReference();

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        // The Drivers' rosters are Monday to Friday; at the weekend the Driver works today by a change of Roster, so the run works on any day.
        _worksToday = await fixture.WorkTodayAsync(Driver, ct);
        Simulator = await fixture.StartSimulatorWithAgentsAsync(
            configureServices: null, ct, SeedEdits.OnlyShippers(Shipper), SeedEdits.OwnFleetOf(Vehicle, Driver));
        var (_, run, _) = await Simulator.RunToEndAsync(Settings, ct, Runs.TripPatience);
        run.Status.Should().Be(SimulationRunStatus.Completed);
        RunId = run.Id;
    }

    public async ValueTask DisposeAsync()
    {
        if (_worksToday is not null)
        {
            await _worksToday.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<LogLine>> LogAsync(CancellationToken cancellationToken) => await Simulator.Requests.LogAsync(RunId, cancellationToken);

    public async Task<SimulationPlan> PlanAsync(CancellationToken cancellationToken)
    {
        await using var scope = Simulator.Services.CreateAsyncScope();
        var run = await scope.ServiceProvider.GetRequiredService<IReadRepository<SimulationRun>>().GetByIdAsync(new SimulationRunId(RunId), cancellationToken);
        return run!.Plan;
    }

    /// <summary>The texts the Driver sent with the action: the Shipment, and the pool and the index the log names for each.</summary>
    public async Task<IReadOnlyList<DriverText>> ReportsAsync(string action, TextCategory category, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(cancellationToken);
        var texts = new List<DriverText>();
        foreach (var line in (await LogAsync(cancellationToken)).Where(line => line.Action == action && line.Outcome == LogOutcome.DriverReported))
        {
            var match = PoolReference().Match(line.Detail ?? string.Empty);
            match.Success.Should().BeTrue($"the line {line.Sequence} names the pool its text was drawn from");
            var pool = plan.Pools.Single(candidate => candidate.Name == match.Groups["pool"].Value.TrimEnd(','));
            pool.Category.Should().Be(category);
            texts.Add(new DriverText(line.ShipmentNumber!, pool, int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        return texts;
    }
}
