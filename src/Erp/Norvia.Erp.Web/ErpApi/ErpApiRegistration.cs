using Azure.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Norvia.Erp.Web.ErpApi;

public static class ErpApiRegistration
{
    /// <summary>
    /// The ERP UI's master-data saves go to the ERP API over HTTP, as the ERP UI's own identity: a token it signs itself locally,
    /// or one from Entra for its managed identity in Azure.
    /// </summary>
    public static IHostApplicationBuilder AddErpApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<ErpApiOptions>().BindConfiguration(ErpApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => (options.Entra is null) != (options.LocalTokens is null), "Configure either ErpApi:Entra or ErpApi:LocalTokens.")
            // Data annotations don't look inside the nested options, so whichever is configured is checked here.
            .Validate(options => options.Entra is null || options.Entra.IsComplete, "ErpApi:Entra needs a Scope and a ManagedIdentityClientId.")
            .Validate(options => options.LocalTokens is null || options.LocalTokens.IsComplete, "ErpApi:LocalTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IErpApiTokens>(services =>
            services.GetRequiredService<IOptions<ErpApiOptions>>().Value switch
            {
                { Entra: { } entra } => new EntraErpApiTokens(
                    new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId)), entra.Scope),
                _ => ActivatorUtilities.CreateInstance<LocalErpApiTokens>(services),
            });

        builder.Services.AddHttpClient<IMasterDataCommands, MasterDataApiClient>((services, http) =>
            http.BaseAddress = services.GetRequiredService<IOptions<ErpApiOptions>>().Value.BaseAddress);

        return builder;
    }
}
