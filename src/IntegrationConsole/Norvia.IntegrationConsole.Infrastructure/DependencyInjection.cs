using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query.Logs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Agents;
using Norvia.IntegrationConsole.Infrastructure.Audit;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;
using Norvia.IntegrationConsole.Infrastructure.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Runs;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure;

public static partial class DependencyInjection
{
    /// <summary>
    /// The client ID of the console's user-assigned managed identity, which the estate Bicep owns. Aspire sets it on the
    /// container app it gives that identity.
    /// </summary>
    public const string ManagedIdentityClientIdKey = "AZURE_CLIENT_ID";

    public static IHostApplicationBuilder AddIntegrationConsoleInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton(ConsoleCredential(builder.Configuration));
        builder.AddWorkflowRuns();
        builder.AddDeadLetterQueues();
        builder.AddAgentClient();
        // Through ServiceDefaults' OpenTelemetry logging, so it reaches the same Application Insights as everything else.
        builder.Services.AddSingleton<IAuditLog, OpenTelemetryAuditLog>();

        // The AppHost gives the console the Aspire dashboard only locally, and only when no deployed estate is named
        // (ADR 0027). Local telemetry then stays on the developer's machine.
        if (builder.Configuration.GetSection(DashboardTrailSourceOptions.SectionName).Exists())
        {
            builder.Services.AddOptions<DashboardTrailSourceOptions>().BindConfiguration(DashboardTrailSourceOptions.SectionName)
                .ValidateDataAnnotations()
                .Validate(
                    options => options.DashboardUrl is null || options.DashboardUrl.IsAbsoluteUri,
                    $"{DashboardTrailSourceOptions.SectionName}:{nameof(DashboardTrailSourceOptions.DashboardUrl)} must be the dashboard's absolute address.")
                .ValidateOnStart();
            builder.Services.AddHttpClient<DashboardTrailSource>((services, http) =>
                http.BaseAddress = services.GetRequiredService<IOptions<DashboardTrailSourceOptions>>().Value.DashboardUrl);
            builder.Services.AddTransient<ITrailSource>(services => services.GetRequiredService<DashboardTrailSource>());
            // No Logic Apps run locally, so the failures are the ones the dashboard holds.
            builder.Services.Replace(ServiceDescriptor.Transient<IFailedRuns>(services => services.GetRequiredService<DashboardTrailSource>()));
            builder.Services.AddTransient<IRefusedCalls>(services => services.GetRequiredService<DashboardTrailSource>());
            return builder;
        }

        // Otherwise Message trails come from the Log Analytics workspace behind the estate's Application Insights, read
        // as the console's credential above. Without an estate the ID is empty and the console still starts; search
        // then says it isn't connected.
        builder.Services.AddOptions<TrailSourceOptions>().BindConfiguration(TrailSourceOptions.SectionName)
            .Validate(
                options => string.IsNullOrEmpty(options.LogAnalyticsWorkspaceId) || Guid.TryParse(options.LogAnalyticsWorkspaceId, out _),
                $"{TrailSourceOptions.SectionName}:{nameof(TrailSourceOptions.LogAnalyticsWorkspaceId)} must be the workspace's ID (its customer ID, a GUID).")
            .ValidateOnStart();
        builder.Services.AddSingleton(services => new LogsQueryClient(services.GetRequiredService<TokenCredential>()));
        builder.Services.AddSingleton<ITrailSource, LogAnalyticsTrailSource>();
        builder.Services.AddSingleton<IRefusedCalls, LogAnalyticsRefusedCalls>();

        return builder;
    }

    /// <summary>
    /// Runs and their bodies come from the estate's Logic Apps, read as the console's credential. Logic Apps exist only in
    /// Azure, so this holds locally too, with the developer's sign-in. Without an estate the ID is empty and the console
    /// still starts; a run then says it can't be opened.
    /// </summary>
    private static void AddWorkflowRuns(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<WorkflowRunsOptions>().BindConfiguration(WorkflowRunsOptions.SectionName)
            .Validate(
                options => string.IsNullOrEmpty(options.ResourceGroupId) || ResourceGroupId().IsMatch(options.ResourceGroupId),
                $"{WorkflowRunsOptions.SectionName}:{nameof(WorkflowRunsOptions.ResourceGroupId)} must be the estate's resource group ID: /subscriptions/{{subscription}}/resourceGroups/{{name}}.")
            .ValidateOnStart();
        builder.Services.AddHttpClient(LogicAppsWorkflowRuns.ManagementClient);
        // A content link is signed, so it is as good as the body it fetches: neither is logged. Traces leave out a URL's
        // query, where the signature is.
        builder.Services.AddHttpClient(LogicAppsWorkflowRuns.ContentsClient).RemoveAllLoggers();
        builder.Services.AddSingleton<LogicAppsWorkflowRuns>();
        builder.Services.AddSingleton<IWorkflowRuns>(services => services.GetRequiredService<LogicAppsWorkflowRuns>());
        builder.Services.AddSingleton<IFailedRuns>(services => services.GetRequiredService<LogicAppsWorkflowRuns>());
    }

    /// <summary>
    /// Dead-letter queues come from the estate's Service Bus, read as the console's credential: Reader on the namespace finds
    /// every queue and subscription and counts them, and a peek reads the oldest message's age. Locally the AppHost gives the
    /// console its emulators, which it lists instead; no local count is ever read from an estate.
    /// </summary>
    private static void AddDeadLetterQueues(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<LocalServiceBusOptions>().BindConfiguration(LocalServiceBusOptions.SectionName);
        builder.Services.AddOptions<EstateServiceBusOptions>().BindConfiguration(EstateServiceBusOptions.SectionName)
            .Validate(
                options => string.IsNullOrEmpty(options.ResourceGroupId) || ResourceGroupId().IsMatch(options.ResourceGroupId),
                $"{EstateServiceBusOptions.SectionName}:{nameof(EstateServiceBusOptions.ResourceGroupId)} must be the estate's resource group ID: /subscriptions/{{subscription}}/resourceGroups/{{name}}.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IServiceBusClients, ServiceBusClients>();
        builder.Services.AddSingleton<IDeadLetterQueues, ServiceBusDeadLetterQueues>();
        if (builder.Configuration.GetSection(LocalServiceBusOptions.SectionName).Exists())
        {
            builder.Services.AddSingleton<IServiceBusEntities>(services => new EmulatorServiceBusEntities(services.GetRequiredService<IOptions<LocalServiceBusOptions>>()));
        }
        else
        {
            builder.Services.AddHttpClient(ArmServiceBusEntities.ManagementClient);
            builder.Services.AddSingleton<IServiceBusEntities, ArmServiceBusEntities>();
        }
    }

    [GeneratedRegex("^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/]+$")]
    private static partial Regex ResourceGroupId();

    /// <summary>
    /// What the adapters call Azure as. The console checks the operator's app role, then reads the estate as its own
    /// managed identity, never with the operator's token. Locally, where it reads a deployed estate, that is the
    /// developer's own sign-in (Azure CLI, Visual Studio and the like).
    /// </summary>
    private static TokenCredential ConsoleCredential(IConfiguration configuration) =>
        configuration[ManagedIdentityClientIdKey] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
            });
}
