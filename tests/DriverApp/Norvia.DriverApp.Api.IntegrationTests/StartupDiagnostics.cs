using System.Globalization;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Norvia.DriverApp.Api.IntegrationTests;

/// <summary>
/// Says why the AppHost did not become healthy: for every resource its state, what each of its health checks reported, and the last lines it logged.
/// Aspire only reports that a wait was cancelled, and in CI the AppHost's containers and processes are gone by the time anyone looks.
/// </summary>
internal static class StartupDiagnostics
{
    private const int LogLinesPerResource = 40;

    // The log service replays what a resource has logged and then waits for more, so reading stops after this.
    private static readonly TimeSpan LogReadWindow = TimeSpan.FromSeconds(2);

    public static async Task<string> DescribeAsync(DistributedApplication app)
    {
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).ToList();
        var notifications = app.ResourceNotifications;
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();

        var sections = await Task.WhenAll(resources.Select(async resource =>
        {
            var lines = await LastLinesAsync(logs, resource.Name);
            return notifications.TryGetCurrentState(resource.Name, out var current)
                ? Format(resource.Name, current.Snapshot, lines)
                : Format(resource.Name, null, lines);
        }));
        return string.Join(Environment.NewLine, sections);
    }

    private static string Format(string name, CustomResourceSnapshot? snapshot, IReadOnlyList<string> logLines)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"== {name}: state {snapshot?.State?.Text ?? "unknown"}, health {snapshot?.HealthStatus?.ToString() ?? "none"}");
        foreach (var report in snapshot?.HealthReports ?? [])
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"   health check {report.Name}: {report.Status?.ToString() ?? "not run yet"} {report.Description}").TrimEnd());
            if (!string.IsNullOrWhiteSpace(report.ExceptionText))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"      {report.ExceptionText.ReplaceLineEndings(" ")}");
            }
        }

        if (snapshot?.ExitCode is { } exitCode)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"   exit code {exitCode}");
        }

        if (logLines.Count == 0)
        {
            text.AppendLine("   (no log lines)");
        }

        foreach (var line in logLines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"   | {line}");
        }

        return text.ToString().TrimEnd();
    }

    private static async Task<IReadOnlyList<string>> LastLinesAsync(ResourceLoggerService logs, string resourceName)
    {
        var lines = new List<string>();
        using var window = new CancellationTokenSource(LogReadWindow);
        try
        {
            await foreach (var batch in logs.WatchAsync(resourceName).WithCancellation(window.Token))
            {
                lines.AddRange(batch.Select(line => line.Content));
            }
        }
        catch (OperationCanceledException)
        {
            // The window is over: these are the lines the resource has logged.
        }

        return lines.TakeLast(LogLinesPerResource).ToList();
    }
}
