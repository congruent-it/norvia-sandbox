using Microsoft.AspNetCore.Authentication;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Web.Operators;

/// <summary>
/// How an Integration operator signs in (ADR 0026). In Azure, Container Apps' Easy Auth signs them in to the console's
/// app registration in Entra ID, and the console reads who they are and their app roles from it. Locally there is no
/// Easy Auth, so the AppHost names a local operator: the developer, with the roles it gives them.
/// </summary>
public static class OperatorSignIn
{
    /// <summary>Everything the console shows: search, Message trails, run details and bodies.</summary>
    public const string ReadPolicy = OperatorRoles.Read;

    public static IHostApplicationBuilder AddOperatorSignIn(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var localOperator = builder.Configuration.GetSection(LocalOperatorOptions.SectionName);
        if (localOperator.Exists())
        {
            // Anywhere else, a configured operator would let anyone in with the roles it names.
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"A local operator ({LocalOperatorOptions.SectionName}) signs in only in Development; in Azure Easy Auth signs operators in.");
            }

            builder.Services.AddOptions<LocalOperatorOptions>().Bind(localOperator).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddAuthentication(LocalOperatorHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LocalOperatorHandler>(LocalOperatorHandler.SchemeName, configureOptions: null);
        }
        else
        {
            builder.Services.AddAuthentication(EasyAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EasyAuthHandler>(EasyAuthHandler.SchemeName, configureOptions: null);
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ReadPolicy, policy => policy.RequireRole(OperatorRoles.Read));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }
}
