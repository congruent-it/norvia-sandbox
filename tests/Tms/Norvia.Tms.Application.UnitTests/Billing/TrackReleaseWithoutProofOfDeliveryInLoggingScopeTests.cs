using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.Billing;

public sealed class TrackReleaseWithoutProofOfDeliveryInLoggingScopeTests : IDisposable
{
    private static readonly ReleaseWithoutProofOfDeliveryCommand Release = new("TO-0000007", "CMR by post", "Jan", "corr-release");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackReleaseWithoutProofOfDeliveryInLoggingScope _behaviour;

    public TrackReleaseWithoutProofOfDeliveryInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackReleaseWithoutProofOfDeliveryInLoggingScope(_loggerFactory.CreateLogger<TrackReleaseWithoutProofOfDeliveryInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Released_LogsTheBillingStateOnTheReleasesOwnTrail()
    {
        var released = new ReleaseWithoutProofOfDeliveryResult("TO-0000007", "SH-0000012", BillingState.Billable, "corr-release");

        await _behaviour.Handle(Release, (_, _) => ValueTask.FromResult<Result<ReleaseWithoutProofOfDeliveryResult>>(released), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("TO-0000007").And.Contain("Billable");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_ReleaseMadeFromASuggestion_NamesTheAgentAndItsRun()
    {
        var released = new ReleaseWithoutProofOfDeliveryResult("TO-0000007", "SH-0000012", BillingState.Billable, "corr-release");

        await _behaviour.Handle(Release with { ActingOn = new Norvia.Agents.AgentRun("customer-service", "run-7") }, (_, _) => ValueTask.FromResult<Result<ReleaseWithoutProofOfDeliveryResult>>(released), CancellationToken.None);

        ScopeKeysOf(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["agentId"] = "customer-service",
            ["agentRunId"] = "run-7",
        });
    }

    [Fact]
    public async Task Handle_ReleaseMadeOnTheEmployeesOwn_NamesNoAgent()
    {
        var released = new ReleaseWithoutProofOfDeliveryResult("TO-0000007", "SH-0000012", BillingState.Billable, "corr-release");

        await _behaviour.Handle(Release, (_, _) => ValueTask.FromResult<Result<ReleaseWithoutProofOfDeliveryResult>>(released), CancellationToken.None);

        ScopeKeysOf(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Keys.Should().NotContain(["agentId", "agentRunId"]);
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode()
    {
        var refused = BillingRefused.Because<ReleaseWithoutProofOfDeliveryResult>(new BillingRefusal(BillingRefusalCode.AlreadyBillable, "Transport Order TO-0000007 is Billable already."));

        await _behaviour.Handle(Release, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("TO-0000007").And.Contain("Billable already");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["outcome"] = "Refused",
            ["failureCode"] = "AlreadyBillable",
        });
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private static Dictionary<string, object?> ScopeKeysOf(FakeLogRecord log) =>
        log.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
}
