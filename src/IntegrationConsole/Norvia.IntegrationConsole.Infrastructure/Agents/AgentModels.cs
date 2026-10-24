using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Scripts;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>The model a local run talks to: the scripted fake, or a developer's own deployment in Foundry when a setting points at it.</summary>
internal static class AgentModels
{
    public static IChatClient For(AgentDefinition definition, AgentModelOptions model, TokenCredential credential) =>
        model.Endpoint is { } endpoint
            ? new AzureOpenAIClient(endpoint, credential).GetChatClient(string.IsNullOrWhiteSpace(model.Deployment) ? definition.Model.Deployment : model.Deployment).AsIChatClient()
            : definition.Name == "integration-operations" ? IntegrationOperationsScript.Model() : throw new InvalidOperationException($"There is no scripted model for the Agent '{definition.Name}'.");
}
