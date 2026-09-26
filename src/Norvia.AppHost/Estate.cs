namespace Norvia.AppHost;

/// <summary>
/// What the apps need from the estate in Azure (ADR 0004). The estate Bicep outputs these, and azd's preprovision
/// hook (infra/provision-estate.sh) stores each one as <c>AZURE_ESTATE_*</c>, which is where azd looks up the value of
/// the Aspire parameter of the same name.
/// </summary>
public sealed class Estate(IDistributedApplicationBuilder builder)
{
    public IResourceBuilder<ParameterResource> ServiceBusNamespace { get; } = builder.AddParameter("estate-service-bus-namespace");

    public IResourceBuilder<ParameterResource> LogAnalyticsWorkspace { get; } = builder.AddParameter("estate-log-analytics-workspace");

    /// <summary>The Log Analytics workspace's ID (its customer ID), which the Integration console searches.</summary>
    public IResourceBuilder<ParameterResource> LogAnalyticsWorkspaceId { get; } = builder.AddParameter("estate-log-analytics-workspace-id");

    /// <summary>
    /// The estate's <c>integrationRetentionDays</c>: how long Application Insights, Log Analytics and run history keep
    /// what they traced (coding guidelines §7).
    /// </summary>
    public IResourceBuilder<ParameterResource> IntegrationRetentionDays { get; } = builder.AddParameter("estate-integration-retention-days");

    /// <summary>
    /// The resource group the estate deploys its Logic Apps into, <c>/subscriptions/{id}/resourceGroups/{name}</c>, where
    /// the Integration console reads their run history.
    /// </summary>
    public IResourceBuilder<ParameterResource> ResourceGroupId { get; } = builder.AddParameter("estate-resource-group-id");

    public IResourceBuilder<ParameterResource> ApplicationInsights { get; } = builder.AddParameter("estate-application-insights");

    /// <summary>The issuer of Entra's v2.0 tokens for the estate's tenant, which is also the authority to trust.</summary>
    public IResourceBuilder<ParameterResource> EntraIssuer { get; } = builder.AddParameter("estate-entra-issuer");

    /// <summary>The TMS API's app registration, which is the audience of the tokens it accepts.</summary>
    public IResourceBuilder<ParameterResource> TmsApiClientId { get; } = builder.AddParameter("estate-tms-api-client-id");

    /// <summary>The intake Logic App's managed identity, the one caller on the Shipper API intake channel.</summary>
    public IResourceBuilder<ParameterResource> IntakeClientId { get; } = builder.AddParameter("estate-intake-client-id");

    public IResourceBuilder<ParameterResource> EntraTenantId { get; } = builder.AddParameter("estate-entra-tenant-id");

    /// <summary>The Norvia Partner API in API Management: <c>https://&lt;API Management&gt;.azure-api.net/partner/v1</c>.</summary>
    public IResourceBuilder<ParameterResource> PartnerApiUrl { get; } = builder.AddParameter("estate-partner-api-url");

    /// <summary>The Norvia Partner API's app registration, the audience of the Personas' tokens.</summary>
    public IResourceBuilder<ParameterResource> PartnerApiClientId { get; } = builder.AddParameter("estate-partner-api-client-id");

    /// <summary>Each Persona's client app: <c>personaId=clientId</c> pairs separated by semicolons.</summary>
    public IResourceBuilder<ParameterResource> PersonaClientIds { get; } = builder.AddParameter("estate-persona-client-ids");

    /// <summary>The Partner portal's managed identity, which every Persona's client app trusts (ADR 0005).</summary>
    public IResourceBuilder<ParameterResource> PartnerPortalIdentity { get; } = builder.AddParameter("estate-partner-portal-identity-name");

    public IResourceBuilder<ParameterResource> PartnerPortalClientId { get; } = builder.AddParameter("estate-partner-portal-client-id");

    /// <summary>The Key Vault that holds each Persona's subscription key as <c>subscription-key-&lt;persona&gt;</c>.</summary>
    public IResourceBuilder<ParameterResource> PartnerPortalKeyVault { get; } = builder.AddParameter("estate-partner-portal-key-vault");

    /// <summary>
    /// The demo UIs' app registration, which Easy Auth signs the presenter in to in front of the TMS UI and the Partner
    /// portal (ADR 0024). Only the users assigned to it get a token.
    /// </summary>
    public IResourceBuilder<ParameterResource> DemoUisClientId { get; } = builder.AddParameter("estate-demo-uis-client-id");

    /// <summary>
    /// The Integration console's app registration, which Easy Auth signs operators in to. Its app roles say what an
    /// operator may do (ADR 0026).
    /// </summary>
    public IResourceBuilder<ParameterResource> IntegrationConsoleClientId { get; } = builder.AddParameter("estate-integration-console-client-id");

    /// <summary>The Integration console's managed identity, which it reads the estate as (ADR 0017).</summary>
    public IResourceBuilder<ParameterResource> IntegrationConsoleIdentity { get; } = builder.AddParameter("estate-integration-console-identity-name");
}
