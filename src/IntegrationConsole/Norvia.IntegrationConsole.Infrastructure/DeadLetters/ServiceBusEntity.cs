namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>A queue or subscription found in a namespace, with how many messages are dead-lettered in it.</summary>
/// <param name="Namespace">Which namespace to open it in (<see cref="IServiceBusClients"/>).</param>
/// <param name="Queue">The queue's name, or the subscription's topic when <paramref name="Subscription"/> is given.</param>
internal sealed record ServiceBusEntity(string Namespace, string Queue, string? Subscription, long DeadLetterCount)
{
    public string Name => Subscription is null ? Queue : $"{Queue}/{Subscription}";
}
