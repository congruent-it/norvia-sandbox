using System.Diagnostics;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>The Simulator's own spans, from the activity source <c>ServiceDefaults</c> exports: the one named after the application.</summary>
internal sealed class SimulatorActivities(string applicationName) : IDisposable
{
    public ActivitySource Source { get; } = new(applicationName);

    public void Dispose() => Source.Dispose();
}
