using Azure.Messaging.ServiceBus;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>Opens a client to a namespace, which the caller disposes: the emulator allows few connections, so none is kept.</summary>
internal interface IServiceBusClients
{
    ServiceBusClient Create(string @namespace);
}
