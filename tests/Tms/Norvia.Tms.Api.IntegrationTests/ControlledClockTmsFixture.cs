using System.Globalization;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// A second TMS whose clock the test sets, as it runs as a process of its own: it reads "now" from a file the fixture
/// writes (<c>Clock:ControlFile</c>), and keeps the system's time until a test sets one. Setting it moves the time of
/// every call to this TMS, so only a test class of its own may use it.
/// </summary>
public class ControlledClockTmsFixture : TmsFixture
{
    private readonly string _clockFile = Path.Combine(Path.GetTempPath(), $"norvia-tms-clock-{Guid.NewGuid():N}.txt");

    /// <summary>Sets the TMS's clock to the instant, and holds it there.</summary>
    public void SetClock(DateTimeOffset now)
    {
        // Written aside and moved over, so the TMS never reads a half-written file.
        var aside = $"{_clockFile}.tmp";
        File.WriteAllText(aside, now.ToString("O", CultureInfo.InvariantCulture));
        File.Move(aside, _clockFile, overwrite: true);
    }

    /// <summary>Gives the TMS the system's time again.</summary>
    public void ResetClock() => File.Delete(_clockFile);

    /// <summary>Restarts the TMS API, as a deployment does, keeping its database and clock. Every test on this fixture sees the restart.</summary>
    public Task RestartAsync(CancellationToken cancellationToken) => RestartTmsAsync(cancellationToken);

    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings["Clock__ControlFile"] = _clockFile;

        // The horizon job looks for a new day every second, so a test that moves the clock past midnight doesn't wait for it.
        settings["PlanningHorizon__CheckInterval"] = "00:00:01";
        return settings;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        ResetClock();
        GC.SuppressFinalize(this);
    }
}
