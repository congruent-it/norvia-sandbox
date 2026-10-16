using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Norvia.DriverApp.Application.Reset;

namespace Norvia.DriverApp.Infrastructure.Reset;

/// <summary>Peeks each configured subscription: a peek reads nothing off the subscription, so the check can't take a message from its consumer.</summary>
internal sealed class ServiceBusQuietCheck(ServiceBusClient client, IOptions<ResetOptions> options) : IQuietCheck
{
    public async Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken)
    {
        foreach (var name in options.Value.QuietSubscriptions)
        {
            var (topic, subscription) = Split(name);
            await using var receiver = client.CreateReceiver(topic, subscription);
            if (await receiver.PeekMessageAsync(cancellationToken: cancellationToken) is not null)
            {
                return name;
            }
        }

        return null;
    }

    private static (string Topic, string Subscription) Split(string name)
    {
        var parts = name.Split('/', 2);
        return parts.Length == 2 && parts.All(part => part.Length > 0)
            ? (parts[0], parts[1])
            : throw new InvalidOperationException($"Reset:QuietSubscriptions entry '{name}' isn't topic/subscription.");
    }
}
