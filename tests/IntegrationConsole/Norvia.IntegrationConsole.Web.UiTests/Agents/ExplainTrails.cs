using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Web.UiTests.Agents;

internal static class ExplainTrails
{
    /// <summary>Makes the telemetry fake answer a trail with a failed hop, and returns the trail's own new correlation ID.</summary>
    public static string Failed(ConsoleWebHost console)
    {
        var correlationId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        console.Port<ITrailSource>().HopsAsync(Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == correlationId), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedHop("run", null, "logic-intake: transport-order-intake", "transport-order-intake", now.AddMinutes(-2),
                TimeSpan.FromSeconds(1.5), Outcome.Failed, "driver-app-unavailable", [], new WorkflowRun("logic-intake", "transport-order-intake", "0858")),
        ]);
        return correlationId;
    }
}
