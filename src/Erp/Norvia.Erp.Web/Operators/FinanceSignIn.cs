using Microsoft.AspNetCore.Authentication;
using Norvia.Erp.Application.Operators;

namespace Norvia.Erp.Web.Operators;

/// <summary>
/// How Finance signs in (as ADR 0026 for the Integration console). In Azure, Container Apps' Easy Auth signs them in to the ERP's app
/// registration in Entra ID, and the ERP reads who they are and their app roles from it. Locally there is no Easy Auth, so the AppHost
/// names a local user: the developer, with the Finance role.
/// </summary>
public static class FinanceSignIn
{
    /// <summary>Only callers with the Finance role reach the UI.</summary>
    public const string FinancePolicy = ErpRoles.Finance;

    public static IHostApplicationBuilder AddFinanceSignIn(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var localUser = builder.Configuration.GetSection(LocalUserOptions.SectionName);
        if (localUser.Exists())
        {
            // Anywhere else, a configured user would let anyone in with the roles it names.
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"A local user ({LocalUserOptions.SectionName}) signs in only in Development; in Azure Easy Auth signs users in.");
            }

            builder.Services.AddOptions<LocalUserOptions>().Bind(localUser).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddAuthentication(LocalUserHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LocalUserHandler>(LocalUserHandler.SchemeName, configureOptions: null);
        }
        else
        {
            builder.Services.AddAuthentication(EasyAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EasyAuthHandler>(EasyAuthHandler.SchemeName, configureOptions: null);
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(FinancePolicy, policy => policy.RequireRole(ErpRoles.Finance));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }
}
