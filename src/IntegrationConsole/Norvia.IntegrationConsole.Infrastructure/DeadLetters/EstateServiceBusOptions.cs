namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>Where the estate's Service Bus is: a namespace in the resource group the estate Bicep deploys into.</summary>
public sealed class EstateServiceBusOptions
{
    public const string SectionName = "Estate";

    /// <summary>The resource group's ID, <c>/subscriptions/{subscription}/resourceGroups/{name}</c>, which the estate Bicep outputs.</summary>
    public string? ResourceGroupId { get; set; }

    /// <summary>The namespace's name, which the estate Bicep outputs. Empty until a developer points the console at a deployed estate.</summary>
    public string? ServiceBusNamespace { get; set; }
}
