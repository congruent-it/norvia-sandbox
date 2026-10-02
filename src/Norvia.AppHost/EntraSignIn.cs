using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;

namespace Norvia.AppHost;

/// <summary>
/// Easy Auth in front of a container app: every browser signs in to an Entra app registration of the estate's first, and
/// Entra issues a token only to the users assigned to it. The demo UIs use it as a gate for the presenter (ADR 0024); the
/// Integration console reads the operator and their app roles from it (ADR 0026). A path outside the gate is one a caller
/// that is no browser must reach, such as a Carrier Persona's booking endpoint, which asks for a credential of its own.
/// </summary>
internal static class EntraSignIn
{
    /// <summary>Container Apps probes these, and they tell nobody more than whether the app is up.</summary>
    private static readonly string[] HealthProbePaths = ["/health", "/alive"];

    public static ContainerAppAuthConfig For(
        AzureResourceInfrastructure infrastructure,
        ContainerApp app,
        IResourceBuilder<ParameterResource> appRegistrationClientId,
        IResourceBuilder<ParameterResource> entraIssuer,
        IEnumerable<string>? pathsOutsideTheGate = null)
    {
        var clientId = appRegistrationClientId.AsProvisioningParameter(infrastructure);
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
                ExcludedPaths = [.. HealthProbePaths, .. pathsOutsideTheGate ?? []],
            },
            HttpSettings = new ContainerAppHttpSettings { RequireHttps = true },
            IdentityProviders = new ContainerAppIdentityProvidersConfiguration
            {
                AzureActiveDirectory = new ContainerAppAzureActiveDirectoryConfiguration
                {
                    IsEnabled = true,
                    // No client secret: Easy Auth then asks Entra for an ID token only, which carries the user's app roles.
                    Registration = new ContainerAppAzureActiveDirectoryRegistrationConfiguration
                    {
                        ClientId = clientId,
                        OpenIdIssuer = entraIssuer.AsProvisioningParameter(infrastructure),
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
