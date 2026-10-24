using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Norvia.DriverApp.Api.IntegrationTests.Faults;

/// <summary>
/// A Driver app of its own for the fault tests: they restart it, which every test on a shared fixture would see, and they read what it logs, so it writes
/// its console as JSON with the scopes the tracking contract puts its keys in.
/// </summary>
public sealed class FaultDriverAppFixture : DriverAppFixture
{
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromMinutes(2);

    protected override Dictionary<string, string> DriverAppApiSettings() => new()
    {
        // Aspire sets the formatter under this exact name, and environment names are case-sensitive on Linux.
        ["LOGGING__CONSOLE__FORMATTERNAME"] = "json",
        ["Logging__Console__FormatterOptions__IncludeScopes"] = "true",
    };

    /// <summary>Restarts the Driver app's API, as a deployment does. Its database is kept; its faults, which live in its process, are not.</summary>
    public async Task RestartDriverAppApiAsync(CancellationToken cancellationToken)
    {
        using var restart = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        restart.CancelAfter(RestartTimeout);

        // Stopping a project reports a failure although the process ends, so what counts is the state it reaches.
        await App.ResourceCommands.ExecuteCommandAsync("driver-app-api", KnownResourceCommands.StopCommand, restart.Token);
        await App.ResourceNotifications.WaitForResourceAsync("driver-app-api", resource => KnownResourceStates.TerminalStates.Contains(resource.Snapshot.State?.Text), restart.Token);
        await ExecuteAsync(KnownResourceCommands.StartCommand, restart.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("driver-app-api", restart.Token);
    }

    // A command can answer before the resource is ready for it, so it is tried again for a moment.
    private async Task ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        ExecuteCommandResult result;
        var attempt = 0;
        do
        {
            result = await App.ResourceCommands.ExecuteCommandAsync("driver-app-api", command, cancellationToken);
            if (!result.Success)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        while (!result.Success && ++attempt < 5);

        result.Success.Should().BeTrue($"{command}: {result.Message}");
    }

    /// <summary>
    /// The tracking keys of the first log record the Driver app wrote for the Trip that carries the key, or null if it wrote none within the wait. The keys
    /// are the scopes' entries: the console's JSON formatter writes each scope's pairs as properties of the scope.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>?> TrackedRecordAsync(string tripNumber, string key, CancellationToken cancellationToken)
    {
        var logs = App.Services.GetRequiredService<ResourceLoggerService>();
        var resource = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "driver-app-api");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await foreach (var batch in logs.WatchAsync(resource).WithCancellation(wait.Token))
            {
                foreach (var line in batch)
                {
                    if (KeysOf(line.Content) is { } keys && keys.GetValueOrDefault("tripNumber") == tripNumber && keys.ContainsKey(key))
                    {
                        return keys;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }

        return null;
    }

    private static Dictionary<string, string>? KeysOf(string line)
    {
        var start = line.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var record = JsonDocument.Parse(line[start..]);
            if (!record.RootElement.TryGetProperty("Scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scope in scopes.EnumerateArray().Where(scope => scope.ValueKind == JsonValueKind.Object))
            {
                foreach (var pair in scope.EnumerateObject().Where(pair => pair.Value.ValueKind == JsonValueKind.String))
                {
                    keys[pair.Name] = pair.Value.GetString()!;
                }
            }

            return keys;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
