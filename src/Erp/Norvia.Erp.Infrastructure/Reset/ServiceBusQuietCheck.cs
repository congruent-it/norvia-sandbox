using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norvia.Erp.Application.Reset;
using Norvia.Erp.Infrastructure.Data;
using Norvia.Erp.Infrastructure.Outbox;

namespace Norvia.Erp.Infrastructure.Reset;

/// <summary>
/// Looks for work still on its way into the ERP: events in its own outbox not yet published, and messages on the subscriptions whose consumers record
/// into the ERP. A peek reads nothing off a subscription, so the check can't take a message from its consumer.
/// </summary>
internal sealed class ServiceBusQuietCheck(IServiceProvider services, ErpDbContext dbContext, IOptions<ResetOptions> options) : IQuietCheck
{
    public const string DefaultConnection = DependencyInjection.MessagingConnectionName;
    public const string Outbox = "outbox (unpublished events)";

    public async Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Set<OutboxMessage>().AnyAsync(message => message.PublishedAt == null, cancellationToken))
        {
            return Outbox;
        }

        foreach (var name in options.Value.QuietSubscriptions)
        {
            var (connection, topic, subscription) = Split(name);
            var client = services.GetRequiredKeyedService<ServiceBusClient>(connection);
            await using var receiver = client.CreateReceiver(topic, subscription);
            if (await receiver.PeekMessageAsync(cancellationToken: cancellationToken) is not null)
            {
                return $"{topic}/{subscription}";
            }
        }

        return null;
    }

    private static (string Connection, string Topic, string Subscription) Split(string name)
    {
        var parts = name.Split('/');
        return parts.All(part => part.Length > 0) ? parts.Length switch
        {
            2 => (DefaultConnection, parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2]),
            _ => throw NotASubscription(name),
        } : throw NotASubscription(name);
    }

    private static InvalidOperationException NotASubscription(string name) =>
        new($"Reset:QuietSubscriptions entry '{name}' isn't [connection/]topic/subscription.");
}
