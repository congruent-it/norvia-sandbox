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

    public IResourceBuilder<ParameterResource> ApplicationInsights { get; } = builder.AddParameter("estate-application-insights");

    /// <summary>The issuer of Entra's v2.0 tokens for the estate's tenant, which is also the authority to trust.</summary>
    public IResourceBuilder<ParameterResource> EntraIssuer { get; } = builder.AddParameter("estate-entra-issuer");

    /// <summary>The TMS API's app registration, which is the audience of the tokens it accepts.</summary>
    public IResourceBuilder<ParameterResource> TmsApiClientId { get; } = builder.AddParameter("estate-tms-api-client-id");

    /// <summary>The intake Logic App's managed identity, the one caller on the Shipper API intake channel.</summary>
    public IResourceBuilder<ParameterResource> IntakeClientId { get; } = builder.AddParameter("estate-intake-client-id");
}
