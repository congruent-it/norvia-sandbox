namespace Norvia.DriverApp.Api.IntegrationTests.Reset;

/// <summary>A Driver app of its own whose reset is switched off, as it is in Azure unless the estate switches it on.</summary>
public sealed class ResetOffDriverAppFixture : DriverAppFixture
{
    protected override Dictionary<string, string> DriverAppApiSettings() => new() { ["Reset__Enabled"] = "false" };
}
