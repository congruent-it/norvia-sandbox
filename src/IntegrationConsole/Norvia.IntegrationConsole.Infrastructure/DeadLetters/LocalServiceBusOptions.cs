namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>
/// The Service Bus emulators the AppHost runs. The emulator accepts only a few connections per namespace, so locally the
/// estate has more than one (see the AppHost), and the console lists them all.
/// </summary>
public sealed class LocalServiceBusOptions
{
    public const string SectionName = "LocalServiceBus";

    /// <summary>The emulators, by the name the AppHost gives them.</summary>
    public Dictionary<string, LocalServiceBusNamespace> Namespaces { get; set; } = [];
}

public sealed class LocalServiceBusNamespace
{
    /// <summary>For peeking: the emulator's AMQP endpoint.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>For listing the entities: the same connection string with the emulator's management port, which the emulator keeps apart.</summary>
    public string ManagementConnectionString { get; set; } = string.Empty;
}
