namespace Norvia.AppHost;

/// <summary>
/// What the apps need from the estate in Azure (ADR 0004). The estate Bicep outputs these, and azd's preprovision
/// hook (infra/provision-estate.ps1) stores each one as <c>AZURE_ESTATE_*</c>, which is where azd looks up the value of
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

    /// <summary>The P12 Driver reports Logic App's name, which the Partner portal's identity may start and stop for the presenter panel's fault.</summary>
    public IResourceBuilder<ParameterResource> DriverReportsLogicApp { get; } = builder.AddParameter("estate-driver-reports-logic-app");

    public IResourceBuilder<ParameterResource> ApplicationInsights { get; } = builder.AddParameter("estate-application-insights");

    /// <summary>The issuer of Entra's v2.0 tokens for the estate's tenant, which is also the authority to trust.</summary>
    public IResourceBuilder<ParameterResource> EntraIssuer { get; } = builder.AddParameter("estate-entra-issuer");

    /// <summary>The TMS API's app registration, which is the audience of the tokens it accepts.</summary>
    public IResourceBuilder<ParameterResource> TmsApiClientId { get; } = builder.AddParameter("estate-tms-api-client-id");

    /// <summary>The intake Logic App's managed identity, the one caller on the Shipper API intake channel.</summary>
    public IResourceBuilder<ParameterResource> IntakeClientId { get; } = builder.AddParameter("estate-intake-client-id");

    /// <summary>
    /// The TMS UI's managed identity, the one principal holding the TMS API's manage role, which it re-rates with.
    /// </summary>
    public IResourceBuilder<ParameterResource> TmsUiIdentity { get; } = builder.AddParameter("estate-tms-ui-identity-name");

    public IResourceBuilder<ParameterResource> TmsUiClientId { get; } = builder.AddParameter("estate-tms-ui-client-id");

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

    /// <summary>The Driver app's managed identity, which reads the credential its Trip endpoint asks of its caller.</summary>
    public IResourceBuilder<ParameterResource> DriverAppIdentity { get; } = builder.AddParameter("estate-driver-app-identity-name");

    /// <summary>The Key Vault that holds the Driver app's Trip endpoint credential as <c>driver-app-endpoint-key</c>.</summary>
    public IResourceBuilder<ParameterResource> DriverAppKeyVault { get; } = builder.AddParameter("estate-driver-app-key-vault");

    /// <summary>The address of the blob service of the Driver app's transit container for the files of a Proof of delivery (P12), which its screens store them in.</summary>
    public IResourceBuilder<ParameterResource> DriverAppBlobEndpoint { get; } = builder.AddParameter("estate-driver-app-blob-endpoint");

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

    /// <summary>
    /// The Driver app's app registration, which Easy Auth signs Drivers in to. Each seeded Driver's ID is one of its app
    /// roles, and the role in a Driver's token is their Driver ID (ADR 0037).
    /// </summary>
    public IResourceBuilder<ParameterResource> DriverAppRegistrationClientId { get; } = builder.AddParameter("estate-driver-app-registration-client-id");

    /// <summary>The Driver app API's app registration, which is the audience of the tokens its outcome endpoint accepts (ADR 0005).</summary>
    public IResourceBuilder<ParameterResource> DriverAppApiClientId { get; } = builder.AddParameter("estate-driver-app-api-client-id");

    /// <summary>The domain of the Entra test users the seeded Drivers sign in as, which the presenter panel shows.</summary>
    public IResourceBuilder<ParameterResource> DriverSignInDomain { get; } = builder.AddParameter("estate-driver-sign-in-domain");

    /// <summary>The password of the Drivers' Entra test users, which the presenter panel shows. A demo credential for demo users that hold nothing else.</summary>
    public IResourceBuilder<ParameterResource> DriverSignInPassword { get; } = builder.AddParameter("estate-driver-sign-in-password", secret: true);

    /// <summary>The Integration console's managed identity, which it reads the estate as (ADR 0017).</summary>
    public IResourceBuilder<ParameterResource> IntegrationConsoleIdentity { get; } = builder.AddParameter("estate-integration-console-identity-name");

    /// <summary>
    /// The Simulator's managed identity, which it runs as (ADR 0042): it holds the TMS API's manage role, and every Persona's and
    /// Driver's client app trusts it.
    /// </summary>
    public IResourceBuilder<ParameterResource> SimulatorIdentity { get; } = builder.AddParameter("estate-simulator-identity-name");

    public IResourceBuilder<ParameterResource> SimulatorClientId { get; } = builder.AddParameter("estate-simulator-client-id");

    /// <summary>The Simulator's app registration, which Easy Auth signs the presenter in to. Its app roles say what an operator may do (ADR 0042).</summary>
    public IResourceBuilder<ParameterResource> SimulatorRegistrationClientId { get; } = builder.AddParameter("estate-simulator-registration-client-id");

    /// <summary>The ERP's managed identity, which it runs as. The estate owns it, as it owns the Simulator's.</summary>
    public IResourceBuilder<ParameterResource> ErpIdentity { get; } = builder.AddParameter("estate-erp-identity-name");

    /// <summary>The ERP's app registration, which Easy Auth signs Finance in to. Its Finance app role says who may use the ERP.</summary>
    public IResourceBuilder<ParameterResource> ErpRegistrationClientId { get; } = builder.AddParameter("estate-erp-registration-client-id");

    /// <summary>The ERP billing API's app registration, which is the audience of the tokens it accepts.</summary>
    public IResourceBuilder<ParameterResource> ErpApiClientId { get; } = builder.AddParameter("estate-erp-api-client-id");

    /// <summary>Each seeded Driver's client app for the Simulator: <c>driverId=clientId</c> pairs separated by semicolons.</summary>
    public IResourceBuilder<ParameterResource> DriverClientIds { get; } = builder.AddParameter("estate-driver-client-ids");

    /// <summary>Whether the Simulator does anything (<c>Simulator:Enabled</c>): the estate decides.</summary>
    public IResourceBuilder<ParameterResource> SimulatorEnabled { get; } = builder.AddParameter("estate-simulator-enabled");

    /// <summary>Whether the TMS API, the Partner portal, the Driver app API and the ERP API take the Simulator's Reset estate (<c>Reset:Enabled</c>): the estate decides, and off by default.</summary>
    public IResourceBuilder<ParameterResource> ResetEnabled { get; } = builder.AddParameter("estate-reset-enabled");

    /// <summary>The hard cap on the orders of a Simulation run (<c>Simulator:MaxOrdersPerRun</c>).</summary>
    public IResourceBuilder<ParameterResource> SimulatorMaxOrdersPerRun { get; } = builder.AddParameter("estate-simulator-max-orders-per-run");

    /// <summary>The cap on requests in flight to the estate (<c>Simulator:MaxConcurrentRequests</c>).</summary>
    public IResourceBuilder<ParameterResource> SimulatorMaxConcurrentRequests { get; } = builder.AddParameter("estate-simulator-max-concurrent-requests");

    /// <summary>Whether the Agents do anything (<c>Agents:Enabled</c>): the estate decides, and off by default.</summary>
    public IResourceBuilder<ParameterResource> AgentsEnabled { get; } = builder.AddParameter("estate-agents-enabled");

    /// <summary>The Foundry project's endpoint, which the Agents' host and the deploy scripts call.</summary>
    public IResourceBuilder<ParameterResource> FoundryProjectEndpoint { get; } = builder.AddParameter("estate-foundry-project-endpoint");

    /// <summary>The Integration console's agent API app registration, which is the audience of the tokens its agent API accepts. It is the only API that holds <c>IntegrationConsole.Agent.Read</c> for API Management's identity.</summary>
    public IResourceBuilder<ParameterResource> IntegrationConsoleAgentApiClientId { get; } = builder.AddParameter("estate-integration-console-agent-api-client-id");

    /// <summary>The TMS API's agent API app registration, which is the audience of the tokens the Customer service agent's view accepts. API Management's identity alone holds <c>TransportOrders.CustomerServiceAgent.Read</c> on it.</summary>
    public IResourceBuilder<ParameterResource> TmsAgentApiClientId { get; } = builder.AddParameter("estate-tms-agent-api-client-id");

    /// <summary>The ERP API's agent API app registration, which is the audience of the tokens the Finance agent view accepts. API Management's identity alone holds <c>CarrierInvoices.FinanceAgent.Read</c> on it.</summary>
    public IResourceBuilder<ParameterResource> ErpAgentApiClientId { get; } = builder.AddParameter("estate-erp-agent-api-client-id");

    /// <summary>API Management's identity (its client ID), the one caller of the console's agent API. Its token names the gateway, so the console reads the Agent's identity from the header the gateway sets.</summary>
    public IResourceBuilder<ParameterResource> ApiManagementClientId { get; } = builder.AddParameter("estate-api-management-client-id");

    /// <summary>The Estate health check's managed identity, which reads the estate as (Reader on the estate's resource group) and asks its Agent as. The Agent's identity is another.</summary>
    public IResourceBuilder<ParameterResource> HealthCheckIdentity { get; } = builder.AddParameter("estate-health-check-identity-name");

    /// <summary>The Estate health check's app registration, which Easy Auth signs Integration operators in to. Its app roles say what an operator may do.</summary>
    public IResourceBuilder<ParameterResource> HealthCheckClientId { get; } = builder.AddParameter("estate-health-check-client-id");

    /// <summary>The Estate health check's agent API app registration, which is the audience of the tokens its agent API accepts. API Management's identity holds <c>HealthCheck.Agent.Read</c> on it.</summary>
    public IResourceBuilder<ParameterResource> HealthCheckAgentApiClientId { get; } = builder.AddParameter("estate-health-check-agent-api-client-id");
}
