using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Application.UnitTests.Reports;

namespace Norvia.DriverApp.Application.UnitTests.Trips;

/// <summary>A Trip version is on the Dispatch's Message trail (P11, coding guidelines §7).</summary>
public sealed class TrackTripVersionInLoggingScopeTests : IDisposable
{
    private static readonly ReceiveTripVersionCommand Version = new(
        "TR-0000012", 2, "corr-dispatch", new DateOnly(2026, 10, 13), new TimeOnly(9, 0), "PT4H", new TripVehicleDto("VEH-0002", "NV-123-A", "RigidTruck"), "DRV-0002", "Joost Bakker", []);

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public TrackTripVersionInLoggingScopeTests() => _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    [Theory]
    [InlineData(ResultStatus.Ok, "Succeeded", null)]
    [InlineData(ResultStatus.Invalid, "Refused", "InvalidTripVersion")]
    [InlineData(ResultStatus.Unavailable, "Failed", "DriverAppUnavailable")]
    public async Task TheOutcomeAndItsFailureCode_AreOnTheTrail(ResultStatus status, string outcome, string? failureCode)
    {
        var behaviour = new TrackTripVersionInLoggingScope(_loggerFactory.CreateLogger<TrackTripVersionInLoggingScope>());
        Result<TripVersionOutcome> result = status switch
        {
            ResultStatus.Ok => TripVersionOutcome.Stored,
            ResultStatus.Invalid => Result.Invalid(new ValidationError("version", "Must be 1 or more.")),
            _ => Result.Unavailable("The Driver app is unavailable."),
        };

        await behaviour.Handle(Version, (_, _) => ValueTask.FromResult(result), CancellationToken.None);

        var keys = ScopeKeys.Of(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject);
        keys.Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-dispatch",
            ["correlationId"] = "corr-dispatch",
            ["tripNumber"] = "TR-0000012",
            ["outcome"] = outcome,
        });
        keys.GetValueOrDefault("failureCode").Should().Be(failureCode);
    }
}
