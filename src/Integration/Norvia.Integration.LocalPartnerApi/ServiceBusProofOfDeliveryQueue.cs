using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The Proof of delivery queue on the Service Bus emulator, or on Service Bus when the stand-in is pointed at it.</summary>
internal sealed class ServiceBusProofOfDeliveryQueue(ServiceBusClient serviceBus, IOptions<PartnerApiOptions> options) : IProofOfDeliveryQueue
{
    public async Task EnqueueAsync(ProofOfDeliveryMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var sender = serviceBus.CreateSender(options.Value.ProofOfDeliveryQueue);
        var queued = new ServiceBusMessage(message.Body) { MessageId = message.MessageId, ContentType = "application/json" };
        foreach (var (name, value) in message.Properties)
        {
            queued.ApplicationProperties[name] = value;
        }

        await sender.SendMessageAsync(queued, cancellationToken);
    }
}
