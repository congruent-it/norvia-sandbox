using Azure.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.TmsApi;

public static class TmsApiRegistration
{
    /// <summary>
    /// The TMS UI's commands go to the TMS API over HTTP, as the TMS UI's own identity: a token it signs itself locally,
    /// or one from Entra for its managed identity in Azure.
    /// </summary>
    public static IHostApplicationBuilder AddTmsApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<TmsApiOptions>().BindConfiguration(TmsApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => (options.Entra is null) != (options.LocalTokens is null), "Configure either TmsApi:Entra or TmsApi:LocalTokens.")
            // Data annotations don't look inside the nested options, so whichever is configured is checked here.
            .Validate(options => options.Entra is null || options.Entra.IsComplete, "TmsApi:Entra needs a Scope and a ManagedIdentityClientId.")
            .Validate(options => options.LocalTokens is null || options.LocalTokens.IsComplete, "TmsApi:LocalTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITmsApiTokens>(services =>
            services.GetRequiredService<IOptions<TmsApiOptions>>().Value switch
            {
                { Entra: { } entra } => new EntraTmsApiTokens(
                    new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId)), entra.Scope),
                _ => ActivatorUtilities.CreateInstance<LocalTmsApiTokens>(services),
            });

        builder.Services.AddHttpClient<ITransportOrderCommands, TmsApiClient>((services, http) =>
            http.BaseAddress = services.GetRequiredService<IOptions<TmsApiOptions>>().Value.BaseAddress);

        return builder;
    }

}
