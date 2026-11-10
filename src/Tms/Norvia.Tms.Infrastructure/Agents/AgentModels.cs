using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Scripts;

namespace Norvia.Tms.Infrastructure.Agents;

/// <summary>The model a local run talks to: the scripted fake, or a developer's own deployment in Foundry when a setting points at it.</summary>
internal static class AgentModels
{
    public static IChatClient For(AgentDefinition definition, AgentModelOptions model) =>
        model.Endpoint is { } endpoint
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential()).GetChatClient(string.IsNullOrWhiteSpace(model.Deployment) ? definition.Model.Deployment : model.Deployment).AsIChatClient()
            : definition.Name == "customer-service" ? CustomerServiceScript.Model()
            : definition.Name == "customer-service-reply" ? CustomerServiceReplyScript.Model()
            : definition.Name == "planner" ? PlannerScript.Model()
            : throw new InvalidOperationException($"There is no scripted model for the Agent '{definition.Name}'.");
}
