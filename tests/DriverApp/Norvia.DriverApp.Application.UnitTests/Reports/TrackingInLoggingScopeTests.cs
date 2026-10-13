using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Application.Reports.ReportOutcome;
using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Reports.ReportStop;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Application.Trips;

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

    private static DeliverCommand Delivery() => new("TR-0000012", "DRV-0002", "SH-0000001", "J. Bakker", DeliveryConditionDto.Clean, null, new ProofFile("image/png", new byte[] { 1 }), []);

    [Fact]
    public async Task ADeliver_IsLoggedWithTheFlowTheTripTheShipmentTheDriverAndTheEventId_AsSucceeded()
    {
        var behaviour = new TrackDeliverInLoggingScope(_loggerFactory.CreateLogger<TrackDeliverInLoggingScope>());

        await behaviour.Handle(Delivery(), (_, _) => ValueTask.FromResult<Result<DeliverResult>>(new DeliverResult(EventId, false)), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-0000012",
            ["shipmentNumber"] = "SH-0000001",
            ["partyId"] = "DRV-0002",
            ["sourceMessageId"] = EventId.ToString(),
            ["outcome"] = "Succeeded",
        });
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, "StopNotFound")]
    [InlineData(ResultStatus.Invalid, "ProofOfDeliveryInvalid")]
    [InlineData(ResultStatus.Conflict, "StopNotAvailable")]
    public async Task ADeliverTheDriverAppDoesNotTake_IsLoggedAsRefused_WithTheReasonAsFailureCode(ResultStatus status, string code)
    {
        var behaviour = new TrackDeliverInLoggingScope(_loggerFactory.CreateLogger<TrackDeliverInLoggingScope>());
        Result<DeliverResult> refused = status switch
        {
            ResultStatus.NotFound => Result.NotFound(),
            ResultStatus.Invalid => Result.Invalid(new ValidationError("signature", "A signature is required.")),
            _ => Result.Conflict("No."),
        };

        await behaviour.Handle(Delivery(), (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = code,
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

    [Fact]
    public async Task AStopReport_IsLoggedWithTheFlowTheTripTheDriverAndTheEventId_AsSucceeded()
    {
        var behaviour = new TrackReportStopInLoggingScope(_loggerFactory.CreateLogger<TrackReportStopInLoggingScope>());
        var command = new ReportStopCommand("TR-0000012", "DRV-0002", "SH-0000001", StopKindDto.Collection, StopActionDto.Collected, null);

        await behaviour.Handle(command, (_, _) => ValueTask.FromResult<Result<ReportStopResult>>(new ReportStopResult(EventId, false)), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-0000012",
            ["partyId"] = "DRV-0002",
            ["sourceMessageId"] = EventId.ToString(),
            ["outcome"] = "Succeeded",
        });
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, "StopNotFound")]
    [InlineData(ResultStatus.Invalid, "StopReportInvalid")]
    [InlineData(ResultStatus.Conflict, "StopNotAvailable")]
    public async Task AStopReportTheDriverAppDoesNotTake_IsLoggedAsRefusedWithItsFailureCode(ResultStatus status, string code)
    {
        var behaviour = new TrackReportStopInLoggingScope(_loggerFactory.CreateLogger<TrackReportStopInLoggingScope>());
        var command = new ReportStopCommand("TR-0000012", "DRV-0002", "SH-0000001", StopKindDto.Delivery, StopActionDto.DeliveryFailed, null);
        Result<ReportStopResult> refused = status switch
        {
            ResultStatus.NotFound => Result.NotFound(),
            ResultStatus.Invalid => Result.Invalid(new ValidationError("remark", "A remark is required.")),
            _ => Result.Conflict("Collect the Shipment first."),
        };

        await behaviour.Handle(command, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = code,
        });
    }
}
