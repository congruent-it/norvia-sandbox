using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>
/// The console's activity source for the Agent run's span, named after the application as ServiceDefaults exports it (coding
/// guidelines §7).
/// </summary>
internal sealed class AgentActivities(IHostEnvironment environment) : IDisposable
{
    public ActivitySource Source { get; } = new(environment.ApplicationName);

    public void Dispose() => Source.Dispose();
}
