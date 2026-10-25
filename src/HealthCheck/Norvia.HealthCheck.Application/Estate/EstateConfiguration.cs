namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// The facts about the estate's configuration that the Checks look at. Locally a snapshot holds them; in Azure the adapter over
/// Resource Graph and Azure Monitor produces the same shape (#392). A Check reads only these facts, never a resource.
/// </summary>
/// <param name="Name">What the facts describe, such as <c>as-found</c>. Not a resource name.</param>
/// <param name="ServiceBusEntities">Every queue and subscription with a dead-letter queue that a Logic App reads.</param>
public sealed record EstateConfiguration(string Name, IReadOnlyList<ServiceBusEntity> ServiceBusEntities);

/// <summary>A queue or subscription, by the name Service Bus reports it under, and whether an alert watches its dead-letter queue.</summary>
public sealed record ServiceBusEntity(string Name, bool HasDeadLetterAlert);
