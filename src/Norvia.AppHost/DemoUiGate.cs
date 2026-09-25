using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;

namespace Norvia.AppHost;

/// <summary>
/// Easy Auth in front of a demo UI's container app (ADR 0024): every browser signs in to the estate's demo UIs app
/// registration first, and Entra issues a token only to the users assigned to it.
/// </summary>
internal static class DemoUiGate
{
    /// <summary>Container Apps probes these, and they tell nobody more than whether the app is up.</summary>
    private static readonly string[] HealthProbePaths = ["/health", "/alive"];

    public static ContainerAppAuthConfig For(AzureResourceInfrastructure infrastructure, ContainerApp app, Estate estate)
    {
        var clientId = estate.DemoUisClientId.AsProvisioningParameter(infrastructure);
        return new ContainerAppAuthConfig("authConfig")
        {
            Parent = app,
            // A container app has one auth config, and it must be called current.
            Name = "current",
            Platform = new ContainerAppAuthPlatform { IsEnabled = true },
            GlobalValidation = new ContainerAppGlobalValidation
            {
                UnauthenticatedClientAction = ContainerAppUnauthenticatedClientActionV2.RedirectToLoginPage,
                RedirectToProvider = "azureactivedirectory",
                ExcludedPaths = [.. HealthProbePaths],
            },
            HttpSettings = new ContainerAppHttpSettings { RequireHttps = true },
            IdentityProviders = new ContainerAppIdentityProvidersConfiguration
            {
                AzureActiveDirectory = new ContainerAppAzureActiveDirectoryConfiguration
                {
                    IsEnabled = true,
                    // No client secret: Easy Auth then asks Entra for an ID token only, which is all a gate needs.
                    Registration = new ContainerAppAzureActiveDirectoryRegistrationConfiguration
                    {
                        ClientId = clientId,
                        OpenIdIssuer = estate.EntraIssuer.AsProvisioningParameter(infrastructure),
                    },
                    Validation = new ContainerAppAzureActiveDirectoryValidationConfiguration
                    {
                        AllowedAudiences = [clientId],
                    },
                },
            },
        };
    }
}
