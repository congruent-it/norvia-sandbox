using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed partial class TrackIntakeInLoggingScopeTests : IDisposable
{
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackIntakeInLoggingScope _behaviour;

    public TrackIntakeInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackIntakeInLoggingScope(_loggerFactory.CreateLogger<TrackIntakeInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Submission_HandlerLogsCarryThePartyId()
    {
        await _behaviour.Handle(Command(), HandlerThatLogs(Accepted()), CancellationToken.None);

        var handlerLog = _logs.Collector.GetSnapshot().Single(log => log.Category == "Handler");
        ScopeKeysOf(handlerLog).Should().Contain("partyId", "SHP-0001");
    }

    [Fact]
    public async Task Handle_SubmissionWithoutShipperId_LeavesThePartyIdOut()
    {
        var command = Command() with { TransportOrder = Samples.CanonicalTransportOrder() with { ShipperId = " " } };

        await _behaviour.Handle(command, HandlerThatLogs(Accepted()), CancellationToken.None);

        var handlerLog = _logs.Collector.GetSnapshot().Single(log => log.Category == "Handler");
        ScopeKeysOf(handlerLog).Should().NotContainKey("partyId", "a key that doesn't apply is left out, never sent empty");
    }

    [Fact]
    public async Task Handle_Accepted_LogsTheOutcomeWithTheTransportOrderNumberInScope()
    {
        await _behaviour.Handle(Command(), HandlerThatLogs(Accepted()), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Single(log => log.Category != "Handler");
        outcome.Message.Should().Contain("TO-0000007").And.Contain("Accepted");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["outcome"] = "Succeeded",
        });
        ScopeKeysOf(outcome).Should().NotContainKey("failureCode");
    }

    [Fact]
    public async Task Handle_Repeat_LogsTheOriginalTransportOrderAsARepeat()
    {
        await _behaviour.Handle(Command(), HandlerThatLogs(SubmitTransportOrderResult.From(Samples.AcceptedOrder(), repeat: true)), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Single(log => log.Category != "Handler");
        outcome.Message.Should().Contain("TO-0000007").And.Contain("repeat");
        ScopeKeysOf(outcome).Should().ContainKey("transportOrderNumber").And.Contain("outcome", "Succeeded");
    }

    [Fact]
    public async Task Handle_Rejected_LogsTheRejectionCodesWithoutATransportOrderNumber()
    {
        await _behaviour.Handle(Command(), HandlerThatLogs(SubmitTransportOrderResult.From(Samples.RejectedOrder())), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Single(log => log.Category != "Handler");
        outcome.Message.Should().Contain("InactiveShipper").And.Contain("GoodsLinesMissing");
        ScopeKeysOf(outcome).Should().NotContainKey("transportOrderNumber", "a key that doesn't apply is left out, never sent empty");
    }

    [Fact]
    public async Task Handle_Rejected_RecordsItRefused_AsTheOtherHopsOfTheIntakeDo()
    {
        // The intake Logic App and the local Partner API record the TMS's 422 for a Rejected booking so too.
        await _behaviour.Handle(Command(), HandlerThatLogs(SubmitTransportOrderResult.From(Samples.RejectedOrder())), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Single(log => log.Category != "Handler");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = "TransportOrderRejected",
        });
    }

    [Fact]
    public async Task Handle_Submission_RecordsNoOutcomeOnTheHandlersLogs()
    {
        // The outcome is known only once the handler has answered.
        await _behaviour.Handle(Command(), HandlerThatLogs(Accepted()), CancellationToken.None);

        var handlerLog = _logs.Collector.GetSnapshot().Single(log => log.Category == "Handler");
        ScopeKeysOf(handlerLog).Should().NotContainKey("outcome");
    }

    [Fact]
    public async Task Handle_Submission_LogsNoConsigneeDetails()
    {
        await _behaviour.Handle(Command(), HandlerThatLogs(Accepted()), CancellationToken.None);

        var logged = _logs.Collector.GetSnapshot()
            .Select(log => log.Message + string.Join(' ', ScopeKeysOf(log).Values))
            .ToList();
        var delivery = Samples.CanonicalTransportOrder().Delivery!;
        logged.Should().NotContain(text =>
            text.Contains(delivery.Consignee!.Name!, StringComparison.Ordinal)
            || text.Contains(delivery.Consignee.ContactName!, StringComparison.Ordinal)
            || text.Contains(delivery.Consignee.Phone!, StringComparison.Ordinal)
            || text.Contains(delivery.Address!.Street!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_Submission_ReturnsWhatTheHandlerReturned()
    {
        var accepted = Accepted();

        var result = await _behaviour.Handle(Command(), HandlerThatLogs(accepted), CancellationToken.None);

        result.Value.Should().Be(accepted.Value);
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private static SubmitTransportOrderCommand Command() =>
        new(Samples.CanonicalTransportOrder(), IntakeChannel.ShipperApi, "corr-1");

    private static Result<SubmitTransportOrderResult> Accepted() => SubmitTransportOrderResult.From(Samples.AcceptedOrder());

    private static Dictionary<string, object?> ScopeKeysOf(FakeLogRecord log) =>
        log.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private MessageHandlerDelegate<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>> HandlerThatLogs(
        Result<SubmitTransportOrderResult> result) =>
        (_, _) =>
        {
            var handlerLogger = _loggerFactory.CreateLogger("Handler");
            LogHandling(handlerLogger);
            return ValueTask.FromResult(result);
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling the submission")]
    private static partial void LogHandling(ILogger logger);
}
