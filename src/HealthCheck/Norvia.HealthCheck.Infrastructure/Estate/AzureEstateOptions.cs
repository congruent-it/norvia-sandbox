using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>
/// Where the estate is when the health check reads it in Azure: the resource group the estate Bicep deploys into and the Service Bus
/// namespace in it. The AppHost sets both for the Azure deployment only; where they are unset the health check reads a snapshot.
/// </summary>
public sealed class AzureEstateOptions
{
    public const string SectionName = "Estate";

    /// <summary>The resource group's ID, <c>/subscriptions/{subscription}/resourceGroups/{name}</c>, which the estate Bicep outputs.</summary>
    [Required]
    [RegularExpression("^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/]+$", ErrorMessage = "Estate:ResourceGroupId must be the estate's resource group ID: /subscriptions/{{subscription}}/resourceGroups/{{name}}.")]
    public string ResourceGroupId { get; set; } = string.Empty;

    /// <summary>The namespace's name, which the estate Bicep outputs.</summary>
    [Required]
    public string ServiceBusNamespace { get; set; } = string.Empty;
}
