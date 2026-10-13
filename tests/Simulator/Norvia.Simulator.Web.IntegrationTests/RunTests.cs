extern alias SimulatorWeb;

using Ardalis.Result;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

[Collection("Simulator runs")]
public sealed class RunTests(SimulatorFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARunOfBookings_CreatesTransportOrdersOrRejectedBookingsInTheTms_WithTheConfiguredBadShare()
    {
        var settings = Runs.Burst(orders: 16, badSharePercent: 25, Runs.NextSeed());
        var preview = (await fixture.Simulator.Requests.PreviewAsync(settings, Ct)).Value;

        var run = await fixture.Simulator.Requests.StartAsync(settings, Ct);
        var finished = await fixture.Simulator.Requests.FinishedAsync(run.Id, Ct);

        finished.Status.Should().Be(SimulationRunStatus.Completed);
        finished.Sent.Should().Be(16);
        preview.BadBookings.Should().Be(4);
        var accepted = await fixture.ShipperReferencesAsync("Accepted", Ct);
        var rejected = await fixture.ShipperReferencesAsync("Rejected", Ct);
        foreach (var row in preview.Rows)
        {
            if (row.Expected == ExpectedAnswer.Rejected)
            {
                rejected.Should().Contain(row.Reference, $"{row.Reference} is expected to be rejected");
            }
            else
            {
                accepted.Should().ContainSingle(reference => reference == row.Reference, $"{row.Reference} is expected to be a Transport Order, once");
            }
        }
    }

    [Fact]
    public async Task EveryLogLineOfARun_SaysWhoActedHowNorviaAnsweredAndWhichCorrelationIdFindsTheMessageTrail()
    {
        var settings = Runs.Burst(orders: 10, badSharePercent: 30, Runs.NextSeed());
        var preview = (await fixture.Simulator.Requests.PreviewAsync(settings, Ct)).Value;

        var run = await fixture.Simulator.Requests.StartAsync(settings, Ct);
        await fixture.Simulator.Requests.FinishedAsync(run.Id, Ct);

        var bookings = (await fixture.Simulator.Requests.LogAsync(run.Id, Ct)).Where(Runs.IsBooking).ToList();
        bookings.Should().HaveCount(10);
        bookings.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId) && !string.IsNullOrWhiteSpace(line.Actor) && line.Action == "Send booking");
        bookings.Select(line => line.CorrelationId).Should().OnlyHaveUniqueItems();
        // A repeated reference is sent twice, so lines are matched by reference and outcome, not by reference alone.
        bookings.Select(line => (line.ShipperReference, line.Actor, line.Outcome))
            .Should().BeEquivalentTo(preview.Rows.Select(row => (ShipperReference: (string?)row.Reference, row.Actor, Outcome: Expected(row.Expected))));
        bookings.Where(line => line.Outcome is LogOutcome.Accepted or LogOutcome.AlreadyBooked).Should().OnlyContain(line => line.TransportOrderNumber != null);
        bookings.Where(line => line.Outcome == LogOutcome.Rejected).Should().OnlyContain(line => line.TransportOrderNumber == null);
    }

    [Fact]
    public async Task TheLogOfARun_CanBeFilteredByOutcomeAndByText()
    {
        var run = await fixture.Simulator.Requests.StartAsync(Runs.Burst(orders: 10, badSharePercent: 30, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.FinishedAsync(run.Id, Ct);
        var log = await fixture.Simulator.Requests.LogAsync(run.Id, Ct);
        var wanted = log.First(line => line.Outcome == LogOutcome.Accepted);

        var accepted = await fixture.Simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Outcome: LogOutcome.Accepted), Ct);
        var byCorrelationId = await fixture.Simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Text: wanted.CorrelationId), Ct);
        var byActor = await fixture.Simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Actor: wanted.Actor), Ct);

        accepted.Should().NotBeEmpty().And.OnlyContain(line => line.Outcome == LogOutcome.Accepted);
        byCorrelationId.Should().ContainSingle().Which.ShipperReference.Should().Be(wanted.ShipperReference);
        byActor.Should().NotBeEmpty().And.OnlyContain(line => line.Actor == wanted.Actor);
    }

    [Fact]
    public async Task TheLogOfARun_SurvivesARefresh_AndASecondViewerSeesTheSame()
    {
        var run = await fixture.Simulator.Requests.StartAsync(Runs.Burst(orders: 6, badSharePercent: 0, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.FinishedAsync(run.Id, Ct);
        var before = await fixture.Simulator.Requests.LogAsync(run.Id, Ct);

        await using var restarted = fixture.StartSimulator();
        var after = await restarted.Requests.LogAsync(run.Id, Ct);

        after.Should().Equal(before);
        (await restarted.Requests.ListRunsAsync(Ct)).Should().Contain(summary => summary.Id == run.Id && summary.Status == SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task ASecondRun_CannotStartWhileOneIsActive_AndCancelStopsSending()
    {
        var first = await fixture.Simulator.Requests.StartAsync(Runs.Paced(orders: 20, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.LogWithAsync(first.Id, Runs.IsBooking, Ct);

        var second = await fixture.Simulator.Requests.StartAsync(Runs.Burst(3, 0, Runs.NextSeed()), "integration tests", Ct);
        var cancelled = await fixture.Simulator.Requests.CancelAsync(new SimulationRunId(first.Id), Ct);
        var log = await fixture.Simulator.Requests.LogWithAsync(first.Id, line => line.Action.StartsWith("Run cancelled", StringComparison.Ordinal), Ct);

        second.Status.Should().Be(ResultStatus.Conflict);
        cancelled.Status.Should().Be(ResultStatus.Ok);
        var run = await fixture.Simulator.Requests.FinishedAsync(first.Id, Ct);
        run.Status.Should().Be(SimulationRunStatus.Cancelled);
        run.Sent.Should().BeLessThan(20, "the rest of the paced run, a booking a minute, was never sent");
        log.Count(Runs.IsBooking).Should().Be(run.Sent);
    }

    [Fact]
    public async Task ARunThatIsCancelled_FreesTheWayForTheNextOne()
    {
        var first = await fixture.Simulator.Requests.StartAsync(Runs.Paced(orders: 20, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.CancelAsync(new SimulationRunId(first.Id), Ct);
        await fixture.Simulator.Requests.LogWithAsync(first.Id, line => line.Action.StartsWith("Run cancelled", StringComparison.Ordinal), Ct);

        var next = await fixture.Simulator.Requests.StartAsync(Runs.Burst(2, 0, Runs.NextSeed()), Ct);

        (await fixture.Simulator.Requests.FinishedAsync(next.Id, Ct)).Status.Should().Be(SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task ClearingTheHistory_RemovesFinishedRunsAndTheirLogs_ButTheRunNumbersKeepCounting()
    {
        var earlier = await fixture.Simulator.Requests.StartAsync(Runs.Burst(2, 0, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.FinishedAsync(earlier.Id, Ct);

        var cleared = await fixture.Simulator.Requests.ClearHistoryAsync(Ct);
        var later = await fixture.Simulator.Requests.StartAsync(Runs.Burst(2, 0, Runs.NextSeed()), Ct);
        await fixture.Simulator.Requests.FinishedAsync(later.Id, Ct);

        cleared.Status.Should().Be(ResultStatus.Ok);
        later.Number.Should().BeGreaterThan(earlier.Number);
        (await fixture.Simulator.Requests.GetRunAsync(new SimulationRunId(earlier.Id), Ct)).Status.Should().Be(ResultStatus.NotFound);
        (await fixture.Simulator.Requests.LogAsync(earlier.Id, Ct)).Should().BeEmpty();
        (await fixture.Simulator.Requests.ListRunsAsync(Ct)).Should().ContainSingle(summary => summary.Id == later.Id);
    }

    [Fact]
    public async Task ClearingTheHistory_WhileARunIsActive_IsRefused()
    {
        var active = await fixture.Simulator.Requests.StartAsync(Runs.Paced(orders: 20, Runs.NextSeed()), Ct);

        var cleared = await fixture.Simulator.Requests.ClearHistoryAsync(Ct);
        await fixture.Simulator.Requests.CancelAsync(new SimulationRunId(active.Id), Ct);
        await fixture.Simulator.Requests.LogWithAsync(active.Id, line => line.Action.StartsWith("Run cancelled", StringComparison.Ordinal), Ct);

        cleared.Status.Should().Be(ResultStatus.Conflict);
    }

    private static LogOutcome Expected(ExpectedAnswer expected) => expected switch
    {
        ExpectedAnswer.Accepted => LogOutcome.Accepted,
        ExpectedAnswer.AlreadyBooked => LogOutcome.AlreadyBooked,
        _ => LogOutcome.Rejected,
    };
}
