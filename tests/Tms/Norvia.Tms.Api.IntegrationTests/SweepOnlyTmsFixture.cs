namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// A second TMS whose acceptances never trigger its rating worker, as if every trigger were missed, and whose worker
/// sweeps every second. A Transport Order it rates was found by the sweep.
/// </summary>
public sealed class SweepOnlyTmsFixture : TmsFixture
{
    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings["Rating__TriggerOnAcceptance"] = "false";
        settings["Rating__SweepInterval"] = "00:00:01";
        return settings;
    }
}
