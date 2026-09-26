using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Runs;
using static Norvia.IntegrationConsole.Infrastructure.UnitTests.Runs.RunHistory;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Runs;

/// <summary>
/// Logic Apps exist only in Azure, so the console always reads runs from the estate's Logic Apps, locally with the
/// developer's own sign-in. Settings that can't be right stop the console at startup (coding guidelines §8).
/// </summary>
public sealed class WorkflowRunsRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:15180")]
    public void AddIntegrationConsoleInfrastructure_ReadsRunsFromTheEstatesLogicApps_WithOrWithoutTheDashboard(string? dashboardUrl)
    {
        Dictionary<string, string?> settings = new() { ["Estate:ResourceGroupId"] = ResourceGroupId };
        if (dashboardUrl is not null)
        {
            settings["LocalTelemetry:DashboardUrl"] = dashboardUrl;
            settings["LocalTelemetry:ApiKey"] = "dashboard-api-key";
        }

        using var services = ServicesWith(settings);

        Start(services);

        services.GetRequiredService<IWorkflowRuns>().Should().BeOfType<LogicAppsWorkflowRuns>();
        services.GetRequiredService<IOptions<WorkflowRunsOptions>>().Value.ResourceGroupId.Should().Be(ResourceGroupId);
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithoutAnEstate_StillStarts()
    {
        using var services = ServicesWith([]);

        var start = () => Start(services);

        start.Should().NotThrow();
    }

    [Theory]
    [InlineData("rg-norvia")]
    [InlineData("/subscriptions/norvia/resourceGroups/rg-norvia")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia/providers/Microsoft.Web/sites/x")]
    public void AddIntegrationConsoleInfrastructure_WithAResourceGroupIdThatIsNone_DoesNotStart(string resourceGroupId)
    {
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = resourceGroupId });

        var start = () => Start(services);

        start.Should().Throw<OptionsValidationException>().WithMessage("*Estate:ResourceGroupId*");
    }

    [Fact]
    public async Task BodiesAsync_NeverLogsABody_NorTheLinkThatFetchesIt()
    {
        // Bodies hold personal data, and a signed link is as good as the body (docs/operations/integration-console.md).
        var history = new RunHistory();
        var input = ContentLink("Map_partner_booking", "ActionInputs");
        history
            .Definition(new JsonObject(), new JsonObject())
            .Run("Succeeded", "2026-09-26T10:00:00Z", "2026-09-26T10:00:01Z", new JsonObject { ["name"] = "manual", ["status"] = "Succeeded" })
            .Answer(ActionAddress("Map_partner_booking"), new JsonObject
            {
                ["name"] = "Map_partner_booking",
                ["properties"] = new JsonObject { ["status"] = "Succeeded", ["inputsLink"] = new JsonObject { ["uri"] = input } },
            })
            .Answer(input, HttpStatusCode.OK, """{"consignee":{"name":"Café De Kroon"}}""");
        var logs = new CapturedLogs();
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = ResourceGroupId }, extra =>
        {
            extra.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            extra.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => history));
        });

        var bodies = await services.GetRequiredService<IWorkflowRuns>().BodiesAsync(
            new WorkflowRun(LogicApp, Workflow, RunId), "Map_partner_booking", TestContext.Current.CancellationToken);

        bodies!.Input.Content.Should().Contain("Café De Kroon");
        logs.Messages.Should().NotBeEmpty("the management API's calls are logged as any call is")
            .And.NotContain(message => message.Contains("Café De Kroon") || message.Contains("sig=") || message.Contains("scaleUnits"));
    }

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings, Action<IServiceCollection>? extra = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddIntegrationConsoleInfrastructure();
        builder.Services.RemoveAll<TokenCredential>();
        var credential = Substitute.For<TokenCredential>();
        credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("console-token", DateTimeOffset.MaxValue));
        builder.Services.AddSingleton(credential);
        extra?.Invoke(builder.Services);
        return builder.Services.BuildServiceProvider();
    }

    // What ValidateOnStart runs when the host starts.
    private static void Start(IServiceProvider services) =>
        services.GetRequiredService<IStartupValidator>().Validate();

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLogs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                logs.Messages.Enqueue(state.ToString() ?? string.Empty);
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Messages.Enqueue(formatter(state, exception));
        }
    }
}
