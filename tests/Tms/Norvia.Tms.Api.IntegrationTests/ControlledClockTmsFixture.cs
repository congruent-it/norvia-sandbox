using System.Globalization;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// A second TMS whose clock the test sets, as it runs as a process of its own: it reads "now" from a file the fixture
/// writes (<c>Clock:ControlFile</c>), and keeps the system's time until a test sets one. Setting it moves the time of
/// every call to this TMS, so only a test class of its own may use it.
/// </summary>
public sealed class ControlledClockTmsFixture : TmsFixture
{
    private readonly string _clockFile = Path.Combine(Path.GetTempPath(), $"norvia-tms-clock-{Guid.NewGuid():N}.txt");

    /// <summary>Sets the TMS's clock to the instant, and holds it there.</summary>
    public void SetClock(DateTimeOffset now) => File.WriteAllText(_clockFile, now.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Gives the TMS the system's time again.</summary>
    public void ResetClock() => File.Delete(_clockFile);

    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings["Clock__ControlFile"] = _clockFile;
        return settings;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        ResetClock();
        GC.SuppressFinalize(this);
    }
}
