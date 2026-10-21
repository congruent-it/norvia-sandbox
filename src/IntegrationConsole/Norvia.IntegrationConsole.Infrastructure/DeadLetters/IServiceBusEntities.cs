namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>Discovers the queues and subscriptions of the namespaces the console reads, with their dead-letter counts.</summary>
internal interface IServiceBusEntities
{
    Task<IReadOnlyList<ServiceBusEntity>> ListAsync(CancellationToken cancellationToken);
}
