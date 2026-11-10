using System.Diagnostics;

namespace Norvia.Integration.BookingDraft.Api.Foundry;

/// <summary>The host's activity source for the Agent run's span, named after the application as ServiceDefaults exports it (coding guidelines §7).</summary>
internal sealed class BookingDraftActivities(IHostEnvironment environment) : IDisposable
{
    public ActivitySource Source { get; } = new(environment.ApplicationName);

    public void Dispose() => Source.Dispose();
}
