using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>Opens clients to the estate's namespace as the console's own identity, and to the local emulators by their connection strings.</summary>
internal sealed class ServiceBusClients(TokenCredential credential, IOptions<LocalServiceBusOptions> local) : IServiceBusClients
{
    public ServiceBusClient Create(string @namespace) =>
        local.Value.Namespaces.TryGetValue(@namespace, out var emulator)
            ? new ServiceBusClient(emulator.ConnectionString)
            : new ServiceBusClient($"{@namespace}.servicebus.windows.net", credential);
}
