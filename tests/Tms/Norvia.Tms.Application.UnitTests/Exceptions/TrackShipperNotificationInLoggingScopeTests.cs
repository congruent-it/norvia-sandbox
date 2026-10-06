using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.NotifyShipper;
using Norvia.Tms.Application.ShipperNotifications.Report;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class TrackShipperNotificationInLoggingScopeTests : IDisposable
{
    private static readonly NotifyShipperCommand Notify = new("EX-0000005", "Delayed", "Anna", "corr-notify");
    private static readonly ReportShipperNotificationSentCommand Sent = new("0198e7c0-0000-7000-8000-000000000001", "corr-notify");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public TrackShipperNotificationInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
    }

    [Fact]
    public async Task Notify_Requested_LogsTheOutcomeOnTheActionsOwnTrail()
    {
        var behaviour = new TrackShipperNotificationInLoggingScope(_loggerFactory.CreateLogger<TrackShipperNotificationInLoggingScope>());
        var requested = new NotifyShipperResult("n-1", "EX-0000005", "SH-0000012", ShipperNotificationStatus.Requested, "corr-notify");

        await behaviour.Handle(Notify, (_, _) => ValueTask.FromResult<Result<NotifyShipperResult>>(requested), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("n-1").And.Contain("EX-0000005");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-notify",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Notify_Refused_LogsTheRefusalWithItsFailureCode()
    {
        var behaviour = new TrackShipperNotificationInLoggingScope(_loggerFactory.CreateLogger<TrackShipperNotificationInLoggingScope>());
        var refused = ExceptionRefused.Because<NotifyShipperResult>(new ExceptionRefusal(ExceptionRefusalCode.ExceptionNotOpen, "Exception EX-0000005 is Resolved, not Open."));

        await behaviour.Handle(Notify, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-notify",
            ["outcome"] = "Refused",
            ["failureCode"] = "ExceptionNotOpen",
        });
    }

    [Theory]
    [InlineData(false, "report recorded")]
    [InlineData(true, "report ignored")]
    public async Task Report_LogsSucceeded_WhetherRecordedOrIgnored_OnTheTrailItContinues(bool ignored, string text)
    {
        var behaviour = new TrackShipperNotificationReportInLoggingScope<ReportShipperNotificationSentCommand>(
            _loggerFactory.CreateLogger<TrackShipperNotificationReportInLoggingScope<ReportShipperNotificationSentCommand>>());
        var outcome = new ShipperNotificationReportResult(Sent.NotificationId, ShipperNotificationStatus.Sent, ignored);

        await behaviour.Handle(Sent, (_, _) => ValueTask.FromResult<Result<ShipperNotificationReportResult>>(outcome), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain(text);
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?> { ["correlationId"] = "corr-notify", ["outcome"] = "Succeeded" });
    }

    [Fact]
    public async Task Report_Refused_LogsTheFailureCode()
    {
        var behaviour = new TrackShipperNotificationReportInLoggingScope<ReportShipperNotificationSentCommand>(
            _loggerFactory.CreateLogger<TrackShipperNotificationReportInLoggingScope<ReportShipperNotificationSentCommand>>());
        var refused = ExceptionRefused.Because<ShipperNotificationReportResult>(ExceptionRefusal.ShipperNotificationNotFound(Sent.NotificationId));

        await behaviour.Handle(Sent, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-notify",
            ["outcome"] = "Refused",
            ["failureCode"] = "ShipperNotificationNotFound",
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
