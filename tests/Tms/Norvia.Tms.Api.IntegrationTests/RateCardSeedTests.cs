using System.Collections.Concurrent;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// A Rate Card or a Carrier Rate Card whose Zones overlap is a broken seed: the TMS refuses to start on it rather than
/// rate against it. Each test runs an AppHost of its own, whose TMS API reads a seed where two Zones claim the same
/// postcode prefix.
/// </summary>
[Trait("AppHost", "Own1")]
public sealed class RateCardSeedTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData("tms.overlapping-zones-seed.json", "Rate Card RC-TEST-OVERLAP")]
    [InlineData("tms.overlapping-carrier-zones-seed.json", "Carrier Rate Card CRC-TEST-OVERLAP")]
    public async Task Start_RateCardWithOverlappingZones_TheTmsApiFailsAtStartup(string seedFile, string card)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"],
            timeout.Token);
        builder.RemoveWhatTheTmsApiTestsDontUse();
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "tms-api"))
            .WithEnvironment("Seed__Path", Path.Combine(AppContext.BaseDirectory, "Seed", seedFile));
        await using var app = await builder.BuildAsync(timeout.Token);

        // Not awaited: starting completes only once every resource has, and what waits for the TMS API never does.
        using var stopStarting = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var starting = app.StartAsync(stopStarting.Token);

        var stopped = await app.ResourceNotifications.WaitForResourceAsync(
            "tms-api", resource => KnownResourceStates.TerminalStates.Contains(resource.Snapshot.State?.Text), timeout.Token);

        stopped.Snapshot.ExitCode.Should().NotBe(0);

        // A resource's logs are replayed from its start, so they can be read after it has stopped.
        var logs = new ConcurrentQueue<string>();
        CollectLogs(app, "tms-api", logs, timeout.Token);
        while (!logs.Any(line => line.Contains("Zones overlap", StringComparison.Ordinal)))
        {
            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }

        logs.Should().Contain(line => line.Contains(card, StringComparison.Ordinal) && line.Contains("Rotterdam and Rijnmond both claim NL 31", StringComparison.Ordinal));

        await stopStarting.CancelAsync();
        await starting.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static void CollectLogs(DistributedApplication app, string resourceName, ConcurrentQueue<string> logs, CancellationToken cancellationToken)
    {
        var resource = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == resourceName);
        _ = Task.Run(async () =>
        {
            await foreach (var batch in app.Services.GetRequiredService<ResourceLoggerService>().WatchAsync(resource).WithCancellation(cancellationToken))
            {
                foreach (var line in batch)
                {
                    logs.Enqueue(line.Content);
                }
            }
        }, cancellationToken);
    }
}
