namespace Norvia.Tms.Api.IntegrationTests.Reset;

/// <summary>
/// A TMS of its own that takes the Simulator's reset (ADR 0039), as it does locally. The AppHost's reset waits for the subscriptions the workers
/// consume, and these tests run without the workers, so the TMS waits for the fixture's own subscriptions instead, which the fixture empties as fast
/// as the TMS publishes. A reset deletes everything the TMS holds, so only the reset tests may use it.
/// </summary>
public sealed class ResetTmsFixture : TmsFixture
{
    /// <summary>The app role only the Simulator's identity holds on the TMS API.</summary>
    public const string ResetRole = "Estate.Reset";

    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings["Reset__Enabled"] = "true";
        var quiet = new[] { TransportOrderEventsTopic, ShipmentEventsTopic, TripEventsTopic, TripEventsTopic };
        for (var index = 0; index < quiet.Length; index++)
        {
            settings[$"Reset__QuietSubscriptions__{index}"] = $"{quiet[index]}/{TestSubscription}";
        }

        return settings;
    }
}

/// <summary>A TMS of its own whose reset is switched off, as it is in Azure unless the estate switches it on.</summary>
public sealed class ResetOffTmsFixture : TmsFixture
{
    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings["Reset__Enabled"] = "false";
        return settings;
    }
}
