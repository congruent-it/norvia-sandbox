using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.UnitTests.Log;

public sealed class LogEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly SimulationRunId Run = SimulationRunId.New();

    private static LogEntry Booking(int sequence, string actor, LogOutcome outcome, string? correlationId = null, string? reference = null, string? orderNumber = null) =>
        LogEntry.Booking(Run, sequence, Now.AddSeconds(sequence), actor, "Send booking", outcome, correlationId, reference, orderNumber, detail: null);

    private static readonly LogEntry[] Entries =
    [
        Booking(1, "Brouwer Dranken", LogOutcome.Accepted, "corr-1", "SIM-1-1", "TO-0000001"),
        Booking(2, "Tuincentrum Groenrijk", LogOutcome.Rejected, "corr-2", "SIM-1-2"),
        Booking(3, "Brouwer Dranken", LogOutcome.AlreadyBooked, "corr-3", "SIM-1-1", "TO-0000001"),
        LogEntry.Booking(SimulationRunId.New(), 1, Now, "Brouwer Dranken", "Send booking", LogOutcome.Accepted, "other-run", "SIM-9-1", null, null),
    ];

    [Fact]
    public void Booking_RecordsWhoSentWhatAndWhatCameBack()
    {
        var entry = Booking(1, "Brouwer Dranken", LogOutcome.Accepted, "corr-1", "SIM-1-1", "TO-0000001");

        entry.Actor.Should().Be("Brouwer Dranken");
        entry.Action.Should().Be("Send booking");
        entry.Outcome.Should().Be(LogOutcome.Accepted);
        entry.CorrelationId.Should().Be("corr-1");
        entry.ShipperReference.Should().Be("SIM-1-1");
        entry.TransportOrderNumber.Should().Be("TO-0000001");
    }

    [Fact]
    public void Carrier_RecordsWhichCarrierActedOnWhichShipmentAndWhatCameBack()
    {
        var entry = LogEntry.Carrier(Run, 4, Now, "Van Dijk Transport", "Answer Carrier booking", LogOutcome.Confirmed, "corr-9", "SIM-1-3", "TO-0000003", "SHP-0000003", "CB-0000042");

        entry.Actor.Should().Be("Van Dijk Transport");
        entry.Action.Should().Be("Answer Carrier booking");
        entry.Outcome.Should().Be(LogOutcome.Confirmed);
        entry.CorrelationId.Should().Be("corr-9");
        entry.ShipperReference.Should().Be("SIM-1-3");
        entry.TransportOrderNumber.Should().Be("TO-0000003");
        entry.ShipmentNumber.Should().Be("SHP-0000003");
        entry.Detail.Should().Be("CB-0000042");
    }

    [Fact]
    public void Outcomes_EachStageOfTheFunnel_HasItsOwn()
    {
        LogOutcomes.Planning.Should().Contain([LogOutcome.Released, LogOutcome.BookingCancelled, LogOutcome.ExceptionResolved]);
        LogOutcomes.Carrier.Should().BeEquivalentTo(
            [LogOutcome.Confirmed, LogOutcome.Declined, LogOutcome.NoAnswer, LogOutcome.TrackingSent, LogOutcome.ProofOfDeliverySent, LogOutcome.CarrierRefused, LogOutcome.CarrierNotReceived]);
        LogOutcomes.Booking.Concat(LogOutcomes.Planning).Concat(LogOutcomes.Carrier).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Outcomes_TheNamesFitTheDatabasesColumn()
    {
        Enum.GetNames<LogOutcome>().Should().OnlyContain(name => name.Length <= 20);
    }

    [Fact]
    public void Booking_ADetailLongerThanTheDatabaseKeeps_IsCutShort()
    {
        var entry = LogEntry.Booking(Run, 1, Now, "A", "Send booking", LogOutcome.Rejected, null, null, null, new string('x', 900));

        entry.Detail.Should().HaveLength(LogEntry.MaxDetailLength);
    }

    [Fact]
    public void Note_IsAnInfoLineOfTheSimulatorItself()
    {
        var entry = LogEntry.Note(Run, 9, Now, "Run cancelled");

        entry.Actor.Should().Be(LogEntry.SimulatorActor);
        entry.Outcome.Should().Be(LogOutcome.Info);
        entry.Action.Should().Be("Run cancelled");
    }

    [Fact]
    public void LogEntriesOfRunSpec_OnlyTheRunsOwnLines_InOrder()
    {
        var lines = new LogEntriesOfRunSpec(Run, new LogFilter()).Evaluate(Entries.Reverse()).ToList();

        lines.Select(line => line.Sequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void LogEntriesOfRunSpec_FilterByActor_KeepsThatActorsLines()
    {
        var lines = new LogEntriesOfRunSpec(Run, new LogFilter(Actor: "Brouwer Dranken")).Evaluate(Entries);

        lines.Select(line => line.Sequence).Should().Equal(1, 3);
    }

    [Fact]
    public void LogEntriesOfRunSpec_FilterByOutcome_KeepsThatOutcome()
    {
        var lines = new LogEntriesOfRunSpec(Run, new LogFilter(Outcome: LogOutcome.Rejected)).Evaluate(Entries);

        lines.Should().ContainSingle().Which.Sequence.Should().Be(2);
    }

    [Theory]
    [InlineData("corr-3", 3)]
    [InlineData("TO-0000001", 1)]
    [InlineData("SIM-1-2", 2)]
    public void LogEntriesOfRunSpec_FilterByText_FindsACorrelationIDAReferenceOrAnOrderNumber(string text, int firstSequence)
    {
        var lines = new LogEntriesOfRunSpec(Run, new LogFilter(Text: text)).Evaluate(Entries);

        lines.First().Sequence.Should().Be(firstSequence);
    }

    [Fact]
    public void LogEntriesOfRunSpec_FilterByNothing_IsNoFilter()
    {
        new LogFilter().IsEmpty.Should().BeTrue();
        new LogFilter(Text: " ").IsEmpty.Should().BeTrue();
        new LogFilter(Actor: "x").IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void BookingOutcomesOfRunSpec_IsTheAnswersOfThatRunsBookingsAndNoNotes()
    {
        var all = Entries.Append(LogEntry.Note(Run, 4, Now, "done"));

        new BookingOutcomesOfRunSpec(Run).Evaluate(all).Should().Equal(LogOutcome.Accepted, LogOutcome.Rejected, LogOutcome.AlreadyBooked);
    }
}
