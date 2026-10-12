using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.DriverApp.Application.Reports.ReportOutcome;
using Norvia.DriverApp.Application.Reports.StartTrip;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>The Driver app's hops are on the Message trail of the Driver's action under the tracking contract's keys (coding guidelines §7).</summary>
public sealed class TrackingInLoggingScopeTests : IDisposable
{
    private static readonly Guid EventId = Guid.Parse("0198e7c0-0000-7000-8000-0000000000c1");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public TrackingInLoggingScopeTests() => _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task AStartTrip_IsLoggedWithTheFlowTheTripTheDriverAndTheEventId_AsSucceeded()
    {
        var behaviour = new TrackStartTripInLoggingScope(_loggerFactory.CreateLogger<TrackStartTripInLoggingScope>());

        await behaviour.Handle(new StartTripCommand("TR-0000012", "DRV-0002"), (_, _) => ValueTask.FromResult<Result<StartTripResult>>(new StartTripResult(EventId, false)), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-0000012",
            ["partyId"] = "DRV-0002",
            ["sourceMessageId"] = EventId.ToString(),
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task AStartTripOfATripTheDriverDoesNotHave_IsLoggedAsRefusedWithTripNotFound()
    {
        var behaviour = new TrackStartTripInLoggingScope(_loggerFactory.CreateLogger<TrackStartTripInLoggingScope>());

        await behaviour.Handle(new StartTripCommand("TR-0000012", "DRV-0001"), (_, _) => ValueTask.FromResult<Result<StartTripResult>>(Result.NotFound()), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = "TripNotFound",
        });
    }

    [Fact]
    public async Task AnOutcomeReport_IsLoggedUnderTheCorrelationIdItNames_AsSucceeded()
    {
        var behaviour = new TrackReportOutcomeInLoggingScope(_loggerFactory.CreateLogger<TrackReportOutcomeInLoggingScope>());
        var command = new ReportOutcomeCommand(EventId, "TR-0000012", "corr-1", ReportOutcomeKind.Recorded, null, null);

        await behaviour.Handle(command, (_, _) => ValueTask.FromResult<Result<ReportOutcomeResult>>(ReportOutcomeResult.Applied), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-execution",
            ["correlationId"] = "corr-1",
            ["tripNumber"] = "TR-0000012",
            ["sourceMessageId"] = EventId.ToString(),
            ["outcome"] = "Succeeded",
        });
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, "ReportNotFound")]
    [InlineData(ResultStatus.Invalid, "InvalidOutcome")]
    public async Task AnOutcomeReportTheDriverAppRefuses_IsLoggedAsRefusedWithItsFailureCode(ResultStatus status, string code)
    {
        var behaviour = new TrackReportOutcomeInLoggingScope(_loggerFactory.CreateLogger<TrackReportOutcomeInLoggingScope>());
        var command = new ReportOutcomeCommand(EventId, "TR-0000012", "corr-1", ReportOutcomeKind.Refused, null, null);
        Result<ReportOutcomeResult> refused = status == ResultStatus.NotFound ? Result.NotFound() : Result.Invalid(new ValidationError("failureCode", "Is required."));

        await behaviour.Handle(command, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = code,
        });
    }
}
